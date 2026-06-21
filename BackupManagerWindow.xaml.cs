using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit
{
    /// <summary>
    /// Displays locally stored level archives and allows users to manage or delete them.
    /// </summary>
    public partial class BackupManagerWindow : Window
    {
        #region State & Data Models
        
        private System.Windows.Interop.HwndSource? _hwndSource;

        private readonly string _backupDir;
        public ObservableCollection<BackupItem> BackupList { get; set; } = new();

        public class BackupItem
        {
            public string? FolderName { get; set; }
            public string? LevelName { get; set; }
            public string? Description { get; set; }
            public string? FullPath { get; set; }
            public string? IconPath { get; set; }
            public string? DateSaved { get; set; }
        }

        #endregion

        #region Initialization & Lifecycle

        public BackupManagerWindow()
        {
            InitializeComponent();
            _backupDir = ConfigManager.BackupDirectory;
            lvBackups.ItemsSource = BackupList;

            LoadBackups();
            this.SourceInitialized += Window_SourceInitialized;
        }

        #endregion

        #region Custom Title Bar Controls

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) 
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        #endregion

        #region Backup Management

        /// <summary>
        /// Scans the local backup directory and parses the metadata from standard PS3 save data formats.
        /// </summary>
        private async void LoadBackups()
        {
            BackupList.Clear();

            if (!Directory.Exists(_backupDir))
            {
                txtStatus.Text = "Backup directory not found.";
                return;
            }

            txtStatus.Text = "Scanning backups...";

            // Offload disk operations to a background thread
            var backups = await Task.Run(() =>
            {
                var temp = new System.Collections.Generic.List<BackupItem>();
                foreach (var folderPath in Directory.EnumerateDirectories(_backupDir))
                {
                    string folderName = Path.GetFileName(folderPath);
                    if (!folderName.Contains("LEVEL", StringComparison.OrdinalIgnoreCase) && 
                        !folderName.Contains("ADVLBP", StringComparison.OrdinalIgnoreCase)) continue;

                    temp.Add(ParseBackupFolder(folderPath, folderName));
                }
                return temp;
            });

            // Update UI list safely on UI thread
            foreach (var item in backups)
            {
                BackupList.Add(item);
            }

            txtStatus.Text = $"Found {BackupList.Count} local level backups.";

            if (BackupList.Any())
            {
                lvBackups.SelectedIndex = 0;
            }
        }

        private BackupItem ParseBackupFolder(string folderPath, string folderName)
        {
            string sfoPath = Path.Combine(folderPath, "PARAM.SFO");
            string iconPath = Path.Combine(folderPath, "ICON0.PNG");
            
            string levelName = "Unknown Level";
            string description = "No description provided.";

            if (File.Exists(sfoPath))
            {
                var data = SfoReader.GetLevelData(sfoPath);
                levelName = data.Title ?? levelName;
                description = data.Description ?? description;
            }

            return new BackupItem
            {
                FolderName = folderName,
                LevelName = levelName,
                Description = description,
                FullPath = folderPath,
                IconPath = iconPath,
                DateSaved = Directory.GetCreationTime(folderPath).ToString("yyyy-MM-dd HH:mm")
            };
        }

        private void LoadIconPreview(string? iconPath)
        {
            if (File.Exists(iconPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    using (var stream = new FileStream(iconPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze(); 
                    }
                    var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                    brush.Freeze(); // Stop WPF Event Leak
                    iconEllipse.Fill = brush;
                    txtIconStatus.Text = "";
                }
                catch
                {
                    iconEllipse.Fill = (Brush)FindResource("BgPrimary");
                    txtIconStatus.Text = "Icon error";
                }
            }
            else
            {
                iconEllipse.Fill = (Brush)FindResource("BgPrimary");
                txtIconStatus.Text = "No icon";
            }
        }

        #endregion

        #region UI Event Handlers

        private void LvBackups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnDelete.IsEnabled = lvBackups.SelectedItems.Count > 0;

            if (lvBackups.SelectedItem is BackupItem selected)
            {
                txtLevelTitle.Text = selected.LevelName;
                txtDescription.Text = selected.Description;
                LoadIconPreview(selected.IconPath);
            } 
            else
            {
                txtLevelTitle.Text = "";
                txtDescription.Text = "";
                iconEllipse.Fill = (Brush)FindResource("BgPrimary");
                txtIconStatus.Text = "Select a backup";
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = lvBackups.SelectedItems.Cast<BackupItem>().ToList();

            bool isConfirmed = CustomDialog.Show(
                this, 
                $"Are you sure you want to permanently delete {selectedItems.Count} backup(s)?", 
                "Confirm Deletion", 
                isYesNo: true);

            if (isConfirmed)
            {
                int deletedCount = 0;
                lvBackups.SelectedIndex = -1;

                foreach (var item in selectedItems)
                {
                    try
                    {
                        if (item.FullPath != null)
                        {
                            Directory.Delete(item.FullPath, true); 
                        }
                        
                        BackupList.Remove(item);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        CustomDialog.Show(this, $"Failed to delete {item.FolderName}.\nError: {ex.Message}", "Error");
                    }
                }

                txtStatus.Text = $"Deleted {deletedCount} backup(s).";
            }
        }

        #endregion

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