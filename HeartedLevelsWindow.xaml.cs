using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit
{
    public partial class HeartedLevelsWindow : Window
    {
        public ObservableCollection<LevelItem> HeartedList { get; set; } = new();
        
        private CancellationTokenSource? _iconCts;
        private long _currentIconRequestId = -1;
        private System.Windows.Interop.HwndSource? _hwndSource;

        public HeartedLevelsWindow()
        {
            InitializeComponent();
            lvHearted.ItemsSource = HeartedList;
            
            LoadHeartedLevels();
            this.SourceInitialized += Window_SourceInitialized;
        }

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        private void LoadHeartedLevels()
        {
            HeartedList.Clear();
            foreach (var item in HeartedLevelsManager.HeartedLevels)
            {
                HeartedList.Add(item);
            }
            txtStatus.Text = $"You have {HeartedList.Count} hearted level(s).";
            iconHeartOverlay.Visibility = Visibility.Hidden;

            if (HeartedList.Any())
            {
                lvHearted.SelectedIndex = 0;
            }
        }

        private async void LvHearted_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnRemove.IsEnabled = lvHearted.SelectedItems.Count > 0;
            btnExtract.IsEnabled = lvHearted.SelectedItems.Count > 0;

            if (lvHearted.SelectedItem is LevelItem selected)
            {
                txtLevelTitle.Text = selected.LevelName;
                txtDescription.Text = selected.Description;
                txtCreator.Text = $"By: {selected.Creator}  |  Game: {selected.Game}";
                iconHeartOverlay.Visibility = Visibility.Visible;
                
                _currentIconRequestId = selected.Id;
                _iconCts?.Cancel();
                _iconCts?.Dispose();
                _iconCts = new CancellationTokenSource();
                
                await LoadIconAsync(selected.IconHash, _iconCts.Token);
            } 
            else
            {
                txtLevelTitle.Text = "";
                txtDescription.Text = "";
                txtCreator.Text = "";
                iconHeartOverlay.Visibility = Visibility.Hidden;
                iconEllipse.Fill = (Brush)FindResource("BgPrimary");
                txtIconStatus.Text = "Select a level\nto view details";
            }
        }

        private async Task LoadIconAsync(string? hash, CancellationToken token)
        {
            iconEllipse.Fill = (Brush)FindResource("BgPrimary");

            if (string.IsNullOrEmpty(hash) || hash.Length <= 8)
            {
                txtIconStatus.Text = "No Icon Available";
                return;
            }

            txtIconStatus.Text = "Loading Icon...";
            long expectedRequestId = _currentIconRequestId;

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
                            byte[] pngBytes = await Task.Run(() => TextureDecoder.DecodeToPngCentered(rawResource), token);

                            if (_currentIconRequestId != expectedRequestId || token.IsCancellationRequested) return;

                            var bmp = new BitmapImage();
                            using (var ms = new MemoryStream(pngBytes))
                            {
                                bmp.BeginInit();
                                bmp.CacheOption = BitmapCacheOption.OnLoad;
                                bmp.StreamSource = ms;
                                bmp.EndInit();
                            }
                            bmp.Freeze();

                            var localBrush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                            localBrush.Freeze();
                            iconEllipse.Fill = localBrush;
                            txtIconStatus.Text = "";
                            return; 
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log("HeartedLevelsWindow.LoadIconAsync (Local Archive)", ex);
                    }
                }

                using var response = await MainWindow.SharedHttpClient.GetAsync($"https://zaprit.fish/icon/{hash}", HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 5242880) throw new InvalidOperationException("Icon too large");
                byte[] imageBytes = await response.Content.ReadAsByteArrayAsync(token);

                if (_currentIconRequestId != expectedRequestId || token.IsCancellationRequested) return;

                var webBmp = new BitmapImage();
                using (var ms = new MemoryStream(imageBytes))
                {
                    webBmp.BeginInit();
                    webBmp.CacheOption = BitmapCacheOption.OnLoad;
                    webBmp.StreamSource = ms;
                    webBmp.EndInit();
                }
                webBmp.Freeze(); 

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
            var selectedItems = lvHearted.SelectedItems.Cast<LevelItem>().ToList();
            if (!selectedItems.Any()) return;

            bool isConfirmed = CustomDialog.Show(
                this, 
                $"Are you sure you want to remove {selectedItems.Count} level(s) from your hearted list?", 
                "Confirm Removal", 
                isYesNo: true);

            if (isConfirmed)
            {
                lvHearted.SelectedIndex = -1;
                foreach (var item in selectedItems)
                {
                    HeartedLevelsManager.Remove(item.Id);
                    HeartedList.Remove(item);
                }
                txtStatus.Text = $"Removed {selectedItems.Count} level(s).";

                if (HeartedList.Any())
                {
                    lvHearted.SelectedIndex = 0;
                }
            }
        }

        private async void BtnExtract_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = lvHearted.SelectedItems.Cast<LevelItem>().ToList();
            if (!selectedItems.Any()) return;

            var progressWin = new ProgressWindow { Owner = this };
            progressWin.Show();
            this.IsEnabled = false;

            int successCount = 0;
            int failureCount = 0;
            bool wasCancelled = false;
            var errorMessages = new List<string>();

            try
            {
                var token = progressWin.CancellationTokenSource.Token;

                for (int i = 0; i < selectedItems.Count; i++)
                {
                    if (token.IsCancellationRequested) 
                    {
                        wasCancelled = true;
                        break;
                    }

                    var lvl = selectedItems[i];
                    string baseStatus = $"[{i + 1}/{selectedItems.Count}] Extracting: {lvl.LevelName}";
                    
                    progressWin.UpdateProgress(0, 1, baseStatus, "Initializing download...");

                    var progressIndicator = new Progress<(int processed, int total, string message)>(report =>
                    {
                        progressWin.UpdateProgress(report.processed, report.total, baseStatus, $"{report.message}\nProgress: {report.processed} / {report.total}");
                    });

                    try
                    {
                        var config = new ExtractionConfig
                        {
                            DownloadServer = ConfigManager.DownloadServer,
                            LocalArchivePath = ConfigManager.LocalArchivePath,
                            MaxParallelDownloads = ConfigManager.MaxParallelDownloads
                        };

                        var result = await AssetDownloader.RunExtractionProcessAsync(lvl, ConfigManager.DatabasePath, ConfigManager.BackupDirectory, MainWindow.SharedHttpClient, config, token, progressIndicator);
                        
                        if (result.Success)
                        {
                            successCount++;
                            lvl.Saved = "✓";
                            
                            if (!SavedLevelsManager.Contains(lvl.Id.ToString()))
                            {
                                SavedLevelsManager.SavedLevels.Add(lvl.Id.ToString());
                            }
                        }
                        else
                        {
                            if (result.ErrorMessage.Contains("cancelled")) wasCancelled = true;
                            else
                            {
                                failureCount++;
                                errorMessages.Add($"'{lvl.LevelName}': {result.ErrorMessage}");
                            }
                        }
                    }
                    finally
                    {
                        AssetDownloader.CleanupLocalArchives();
                    }
                }
                
                if (successCount > 0)
                {
                    SavedLevelsManager.Save();
                }
            }
            finally
            {
                progressWin.Close();
                this.IsEnabled = true;
                AssetDownloader.CleanupLocalArchives();
            }

            txtStatus.Text = $"Batch complete. {successCount} packed. {failureCount} failed.";

            if (failureCount > 0)
            {
                string errors = string.Join("\n\n", errorMessages);
                CustomDialog.Show(this, $"Failed to download/pack {failureCount} level(s).\n\nReasons:\n{errors}", "Extraction Failed", false);
            }

            if (successCount > 0)
            {
                string msg = wasCancelled ? $"Cancelled! However, {successCount} level(s) were successfully packed before cancellation.\n\nOpen backup folder?" 
                                          : $"Successfully packed {successCount} level(s)!\n\nOpen backup folder?";

                if (CustomDialog.Show(this, msg, "Finished", true))
                {
                    string fullPath = Path.GetFullPath(ConfigManager.BackupDirectory);
                    if (Directory.Exists(fullPath)) Process.Start("explorer.exe", $"\"{fullPath}\"");
                }
            }
        }

        #region Win32 Interop (Borderless Window Support)

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
            public POINT ptReserved; 
            public POINT ptMaxSize; 
            public POINT ptMaxPosition; 
            public POINT ptMinTrackSize; 
            public POINT ptMaxTrackSize; 
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        #endregion
    }
}