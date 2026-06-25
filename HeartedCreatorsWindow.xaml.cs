using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit
{
    public partial class HeartedCreatorsWindow : Window
    {
        public ObservableCollection<UserItem> HeartedList { get; set; } = new();
        
        private CancellationTokenSource? _iconCts;
        private long _currentIconRequestId = -1;
        private System.Windows.Interop.HwndSource? _hwndSource;

        public HeartedCreatorsWindow()
        {
            InitializeComponent();
            lvHearted.ItemsSource = HeartedList;
            
            LoadHeartedCreators();
            this.SourceInitialized += Window_SourceInitialized;
        }

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        private void LoadHeartedCreators()
        {
            HeartedList.Clear();
            foreach (var item in HeartedCreatorsManager.HeartedCreators)
            {
                HeartedList.Add(item);
            }
            txtStatus.Text = $"You have {HeartedList.Count} hearted creator(s).";
            iconHeartOverlay.Visibility = Visibility.Hidden;

            if (HeartedList.Any())
            {
                lvHearted.SelectedIndex = 0;
            }
        }

        private async void LvHearted_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnRemove.IsEnabled = lvHearted.SelectedItems.Count > 0;
            btnViewUserLevels.IsEnabled = lvHearted.SelectedItems.Count > 0;
            btnDownloadAllLevels.IsEnabled = lvHearted.SelectedItems.Count > 0;

            if (lvHearted.SelectedItem is UserItem selected)
            {
                txtUserNpHandle.Text = selected.NpHandle;
                txtUserStats.Text = $"Hearts: {selected.HeartCount}  |  Total Levels: {selected.TotalLevels}";
                txtUserSummary.Text = $"Published Level slots summary:\n" +
                                      $"• LBP1 Slots: {selected.Lbp1UsedSlots}\n" +
                                      $"• LBP2 Slots: {selected.Lbp2UsedSlots}\n" +
                                      $"• LBP3 Slots: {selected.Lbp3UsedSlots}";
                iconHeartOverlay.Visibility = Visibility.Visible;
                
                _currentIconRequestId = selected.NpHandle.GetHashCode();
                _iconCts?.Cancel();
                _iconCts?.Dispose();
                _iconCts = new CancellationTokenSource();
                
                await LoadUserIconAsync(selected.IconHash, selected.NpHandle, _iconCts.Token);
            } 
            else
            {
                txtUserNpHandle.Text = "";
                txtUserStats.Text = "";
                txtUserSummary.Text = "";
                iconHeartOverlay.Visibility = Visibility.Hidden;
                iconEllipse.Fill = (Brush)FindResource("BgPrimary");
                txtIconStatus.Text = "Select a creator\nto view details";
            }
        }

        private async Task LoadUserIconAsync(string? hash, string npHandle, CancellationToken token)
        {
            iconEllipse.Fill = (Brush)FindResource("BgPrimary");

            if (string.IsNullOrEmpty(hash) || hash.Length <= 8)
            {
                txtIconStatus.Text = "No Icon Available";
                return;
            }

            txtIconStatus.Text = "Loading Icon...";
            long expectedRequestId = npHandle.GetHashCode();

            try
            {
                bool useLocalArchive = ConfigManager.DownloadServer.ToLower() == "local" && !string.IsNullOrWhiteSpace(ConfigManager.LocalArchivePath);

                if (useLocalArchive)
                {
                    try
                    {
                        byte[]? rawResource = await AssetDownloader.ExtractLocalArchiveToMemoryAsync(hash, ConfigManager.LocalArchivePath, token);

                        if (rawResource != null)
                        {
                            var bmp = await Task.Run(() => TextureDecoder.DecodeToBitmapSourceCentered(rawResource), token);

                            if (_currentIconRequestId != expectedRequestId || token.IsCancellationRequested || bmp == null) return;

                            var localBrush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                            localBrush.Freeze();
                            iconEllipse.Fill = localBrush;
                            txtIconStatus.Text = "";
                            return; 
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log("HeartedCreatorsWindow.LoadUserIconAsync (Local Archive)", ex);
                    }
                }

                string server = ConfigManager.DownloadServer;
                string url = AssetDownloader.GetDownloadUrl(hash, server);
                if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("Invalid icon hash");

                using var response = await MainWindow.SharedHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 5242880) throw new InvalidOperationException("Icon too large");
                byte[] rawBytes = await response.Content.ReadAsByteArrayAsync(token);

                if (_currentIconRequestId != expectedRequestId || token.IsCancellationRequested) return;

                var webBmp = await Task.Run(() => TextureDecoder.DecodeToBitmapSourceCentered(rawBytes), token);
                if (webBmp == null) throw new InvalidDataException("Failed to decode image.");

                var brush = new ImageBrush(webBmp) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
                iconEllipse.Fill = brush;
                txtIconStatus.Text = "";
            }
            catch (OperationCanceledException) { }
            catch
            {
                if (_currentIconRequestId == expectedRequestId)
                {
                    txtIconStatus.Text = "Icon offline\nor missing.";
                }
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = lvHearted.SelectedItems.Cast<UserItem>().ToList();
            if (!selectedItems.Any()) return;

            bool isConfirmed = CustomDialog.Show(
                this, 
                $"Are you sure you want to remove {selectedItems.Count} creator(s) from your hearted list?", 
                "Confirm Removal", 
                isYesNo: true);

            if (isConfirmed)
            {
                lvHearted.SelectedIndex = -1;
                foreach (var item in selectedItems)
                {
                    HeartedCreatorsManager.Remove(item.NpHandle);
                    HeartedList.Remove(item);
                }
                txtStatus.Text = $"Removed {selectedItems.Count} creator(s).";

                if (HeartedList.Any())
                {
                    lvHearted.SelectedIndex = 0;
                }
            }
        }

        private void BtnViewUserLevels_Click(object sender, RoutedEventArgs e)
        {
            if (lvHearted.SelectedItem is UserItem selectedUser && this.Owner is MainWindow mainWindow)
            {
                this.Close();
                mainWindow.InitiateCreatorSearch(selectedUser.NpHandle);
            }
        }

        private async void BtnDownloadAllLevels_Click(object sender, RoutedEventArgs e)
        {
            if (lvHearted.SelectedItem is UserItem selectedUser && this.Owner is MainWindow mainWindow)
            {
                this.Close();
                await mainWindow.InitiateBatchDownloadAsync(selectedUser);
            }
        }

        #region Win32 Interop
        
        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WindowProc);
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0024) 
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = (MINMAXINFO)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;
            int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO));
                GetMonitorInfo(monitor, ref monitorInfo);

                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;

                mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
                mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);

                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _iconCts?.Cancel();
            _iconCts?.Dispose();

            _hwndSource?.RemoveHook(WindowProc);
            _hwndSource = null;
            base.OnClosed(e);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct MINMAXINFO 
        { 
            public POINT ptReserved; public POINT ptMaxSize; public POINT ptMaxPosition; public POINT ptMinTrackSize; public POINT ptMaxTrackSize; 
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        #endregion
    }
}