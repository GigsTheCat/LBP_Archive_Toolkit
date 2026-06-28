using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Services;
using LbpArchiveToolkit.Utils;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
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
                return Directory.EnumerateDirectories(_backupDir)
                    .AsParallel()
                    .Where(folderPath =>
                    {
                        string folderName = Path.GetFileName(folderPath);
                        return folderName.Contains("LEVEL", StringComparison.OrdinalIgnoreCase) ||
                               folderName.Contains("ADVLBP", StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(folderPath => ParseBackupFolder(folderPath, Path.GetFileName(folderPath)))
                    .OrderByDescending(b => b.DateSaved)
                    .ToList();
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

                int byIndex = levelName.LastIndexOf(" by ");
                if (byIndex >= 0)
                {
                    levelName = levelName.Substring(0, byIndex);
                }

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
                    var bitmap = LbpArchiveToolkit.Utils.TextureDecoder.LoadBitmapImage(iconPath);
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
            btnEdit.IsEnabled = lvBackups.SelectedItems.Count == 1;

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

        private async void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (lvBackups.SelectedItem is BackupItem selected && selected.FullPath != null)
            {
                var dialog = new EditInfoDialog(selected.LevelName ?? "", selected.Description ?? "", selected.IconPath)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() == true)
                {
                    string newName = dialog.LevelName;
                    string newDesc = dialog.Description;
                    string? newIcon = dialog.NewIconPath;

                    if (newName == selected.LevelName && newDesc == selected.Description && newIcon == null) return;

                    txtStatus.Text = "Updating and re-encrypting backup...";
                    this.IsEnabled = false;

                    try
                    {
                        await Task.Run(() => SaveDataBuilder.UpdateLevelInfo(selected.FullPath, newName, newDesc, newIcon));

                        selected.LevelName = newName;
                        selected.Description = newDesc;
                        txtLevelTitle.Text = newName;
                        txtDescription.Text = newDesc;
                        lvBackups.Items.Refresh();

                        if (newIcon != null)
                        {
                            LoadIconPreview(selected.IconPath);
                        }

                        txtStatus.Text = "Level info updated successfully!";
                    }
                    catch (Exception ex)
                    {
                        CustomDialog.Show(this, $"Failed to update info:\n{ex.Message}", "Error");
                        txtStatus.Text = "Update failed.";
                    }
                    finally
                    {
                        this.IsEnabled = true;
                        if (!string.IsNullOrEmpty(newIcon) && newIcon.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(newIcon); } catch (Exception ex) { LogManager.Log("BackupManagerWindow.BtnEdit_Click.Cleanup", ex); }
                        }
                    }
                }
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
                // Capture the minimum selected index to use as a fallback target
                int fallbackIndex = selectedItems
                    .Select(item => BackupList.IndexOf(item))
                    .Where(idx => idx >= 0)
                    .DefaultIfEmpty(-1)
                    .Min();

                int deletedCount = 0;

                // Clear selection temporarily to avoid UI layout overhead while mutating the collection
                lvBackups.SelectedIndex = -1;

                foreach (var item in selectedItems)
                {
                    try
                    {
                        if (item.FullPath != null)
                        {
                            string resolvedPath = Path.GetFullPath(item.FullPath);
                            string resolvedBackupDir = Path.GetFullPath(_backupDir);
                            string separator = Path.DirectorySeparatorChar.ToString();

                            if (!resolvedBackupDir.EndsWith(separator))
                            {
                                resolvedBackupDir += separator;
                            }

                            if (!resolvedPath.StartsWith(resolvedBackupDir, StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidOperationException("Path traversal detected; delete target resides outside the backup directory.");
                            }

                            Directory.Delete(resolvedPath, true);
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

                // Auto-select the next logical backup item
                if (BackupList.Any())
                {
                    if (fallbackIndex < 0) fallbackIndex = 0;
                    if (fallbackIndex >= BackupList.Count) fallbackIndex = BackupList.Count - 1;

                    lvBackups.SelectedIndex = fallbackIndex;
                    lvBackups.ScrollIntoView(BackupList[fallbackIndex]);
                }
            }
        }

        #endregion

    }
}