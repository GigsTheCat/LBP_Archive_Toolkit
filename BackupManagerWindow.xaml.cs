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
        private void LoadBackups()
        {
            BackupList.Clear();

            if (!Directory.Exists(_backupDir))
            {
                txtStatus.Text = "Backup directory not found.";
                return;
            }

            foreach (var folderPath in Directory.EnumerateDirectories(_backupDir))
            {
                string folderName = Path.GetFileName(folderPath);
                if (!folderName.Contains("LEVEL", StringComparison.OrdinalIgnoreCase)) continue;

                BackupList.Add(ParseBackupFolder(folderPath, folderName));
            }

            txtStatus.Text = $"Found {BackupList.Count} local level backups.";
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
                    
                    iconEllipse.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                    txtIconStatus.Text = "";
                }
                catch
                {
                    iconEllipse.Fill = (SolidColorBrush)FindResource("BgPrimary");
                    txtIconStatus.Text = "Icon error";
                }
            }
            else
            {
                iconEllipse.Fill = (SolidColorBrush)FindResource("BgPrimary");
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
                iconEllipse.Fill = (SolidColorBrush)FindResource("BgPrimary");
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
    }
}