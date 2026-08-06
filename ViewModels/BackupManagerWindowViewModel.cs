using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Services;
using LbpArchiveToolkit.Utils;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LbpArchiveToolkit.ViewModels
{
    public class BackupItemViewModel : ViewModelBase
    {
        public string? FolderName { get; set; }
        
        public string? LevelName { get; set => SetProperty(ref field, value); }
        public string? Description { get; set => SetProperty(ref field, value); }
        
        public string? Creator { get; set; }
        public string? Game { get; set; }
        public string? FullPath { get; set; }
        public string? IconPath { get; set; }
        public string? DateSaved { get; set; }

        public bool? IsLocked { get; set; }
        public bool? IsSubLevel { get; set; }
        public bool? IsShareable { get; set; }
    }

    public class BackupManagerWindowViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;
        private string _backupDir;
        private bool _isBusy;

        public BulkObservableCollection<BackupItemViewModel> BackupList { get; } = new();

        public BackupItemViewModel? SelectedBackup
        {
            get;
            set { if (SetProperty(ref field, value)) UpdateSelectionDetails(); }
        }

        public string StatusText { get; set => SetProperty(ref field, value); } = "Ready.";
        public string LevelTitle { get; set => SetProperty(ref field, value); } = "";
        public string LevelDescription { get; set => SetProperty(ref field, value); } = "";
        public Brush IconFill { get; set => SetProperty(ref field, value); } = null!;
        public Brush OriginalIconFill { get; set => SetProperty(ref field, value); } = null!;
        public Visibility IconLockVisibility { get; set => SetProperty(ref field, value); } = Visibility.Hidden;
        public double IconScale { get; set => SetProperty(ref field, value); } = 1.0;
        public string IconStatusText { get; set => SetProperty(ref field, value); } = "Select a backup\nto view details";

        private CancellationTokenSource? _sltCts;

        public ICommand ViewTexturesCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand MoveCommand { get; }
        public ICommand ChangeDirCommand { get; }

        public BackupManagerWindowViewModel(IViewService viewService)
        {
            _viewService = viewService;
            _backupDir = ConfigManager.BackupDirectory;
            IconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));
            OriginalIconFill = IconFill;

            ViewTexturesCommand = new RelayCommand(ExecuteViewTextures, CanExecuteEdit);
            EditCommand = new RelayCommand(ExecuteEdit, CanExecuteEdit);
            DeleteCommand = new RelayCommand(ExecuteDelete, CanExecuteMultipleAction);
            MoveCommand = new RelayCommand(ExecuteMove, CanExecuteMultipleAction);
            ChangeDirCommand = new RelayCommand(ExecuteChangeDir);

            LoadBackups();
        }

        private bool CanExecuteEdit(object? parameter) => !_isBusy && parameter is IList items && items.Count == 1;
        private bool CanExecuteMultipleAction(object? parameter) => !_isBusy && parameter is IList items && items.Count > 0;

        private async void LoadBackups()
        {
            BackupList.Clear();

            if (!Directory.Exists(_backupDir))
            {
                StatusText = "Backup directory not found.";
                return;
            }

            StatusText = "Scanning backups...";

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

            BackupList.AddRange(backups);

            StatusText = $"Found {BackupList.Count} local level backups.";

            if (BackupList.Any()) SelectedBackup = BackupList[0];
        }

        private BackupItemViewModel ParseBackupFolder(string folderPath, string folderName)
        {
            string sfoPath = Path.Combine(folderPath, "PARAM.SFO");
            string iconPath = Path.Combine(folderPath, "ICON0.PNG");

            string levelName = "Unknown Level";
            string creator = "Unknown";
            string description = "No description provided.";
            string game = "Unknown";

            if (folderName.Contains("00141") || folderName.Contains("98148") || folderName.Contains("30018")) game = "LBP1";
            else if (folderName.Contains("00850") || folderName.Contains("98245") || folderName.Contains("30058")) game = "LBP2";
            else if (folderName.Contains("01663") || folderName.Contains("98362") || folderName.Contains("30095")) game = "LBP3";

            if (File.Exists(sfoPath))
            {
                var data = SfoReader.GetLevelData(sfoPath);
                levelName = data.Title ?? levelName;

                int byIndex = levelName.LastIndexOf(" by ");
                if (byIndex >= 0)
                {
                    creator = levelName.Substring(byIndex + 4);
                    levelName = levelName.Substring(0, byIndex);
                }

                description = data.Description ?? description;
            }

            return new BackupItemViewModel
            {
                FolderName = folderName,
                LevelName = levelName,
                Creator = creator,
                Game = game,
                Description = description,
                FullPath = folderPath,
                IconPath = iconPath,
                DateSaved = Directory.GetCreationTime(folderPath).ToString("yyyy-MM-dd HH:mm")
            };
        }

        private async void UpdateSelectionDetails()
        {
            var selected = SelectedBackup;
            if (selected != null)
            {
                LevelTitle = $"{selected.LevelName} by {selected.Creator}";
                LevelDescription = selected.Description ?? "";
                LoadIconPreview(selected.IconPath);

                IconLockVisibility = (selected.IsLocked == true) ? Visibility.Visible : Visibility.Hidden;
                IconScale = (selected.IsSubLevel == true) ? 0.85 : 1.0;

                if (selected.IsLocked == true && OriginalIconFill is ImageBrush cacheImgBrush && cacheImgBrush.ImageSource is System.Windows.Media.Imaging.BitmapSource cacheBmp)
                {
                    var grayscaleBmp = new System.Windows.Media.Imaging.FormatConvertedBitmap(cacheBmp, System.Windows.Media.PixelFormats.Gray8, null, 0);
                    grayscaleBmp.Freeze();
                    var grayBrush = new ImageBrush(grayscaleBmp) { Stretch = Stretch.UniformToFill };
                    grayBrush.Freeze();
                    IconFill = grayBrush;
                }

                if (!selected.IsLocked.HasValue)
                {
                    _sltCts?.Cancel();
                    _sltCts = new CancellationTokenSource();
                    var token = _sltCts.Token;

                    try
                    {
                        bool isLocked = false;
                        bool isSubLevel = false;
                        bool isShareable = true;

                        await Task.Run(() => {
                            var (_, _, _, sltHash, hashes) = Far4Archive.ReadSaveArchive(selected.FullPath!);
                            string oldHashHex = Convert.ToHexStringLower(sltHash);
                            if (hashes.TryGetValue(oldHashHex, out byte[]? sltData)) {
                                sltData = SltbProcessor.DecompressSltData(sltData);
                                (isLocked, isSubLevel, isShareable) = SltbProcessor.ReadSlotBools(sltData!);
                            }
                        }, token);

                        if (!token.IsCancellationRequested)
                        {
                            selected.IsLocked = isLocked;
                            selected.IsSubLevel = isSubLevel;
                            selected.IsShareable = isShareable;

                            IconLockVisibility = isLocked ? Visibility.Visible : Visibility.Hidden;
                            IconScale = isSubLevel ? 0.85 : 1.0;

                            if (isLocked && OriginalIconFill is ImageBrush imgBrush && imgBrush.ImageSource is System.Windows.Media.Imaging.BitmapSource bmp)
                            {
                                var grayscaleBmp = new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, System.Windows.Media.PixelFormats.Gray8, null, 0);
                                grayscaleBmp.Freeze();
                                var grayBrush = new ImageBrush(grayscaleBmp) { Stretch = Stretch.UniformToFill };
                                grayBrush.Freeze();
                                IconFill = grayBrush;
                            }
                        }
                    }
                    catch { }
                }
            }
            else
            {
                LevelTitle = "";
                LevelDescription = "";
                IconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));
                OriginalIconFill = IconFill;
                IconLockVisibility = Visibility.Hidden;
                IconScale = 1.0;
                IconStatusText = "Select a backup\nto view details";
            }
        }

        private void LoadIconPreview(string? iconPath)
        {
            if (File.Exists(iconPath))
            {
                try
                {
                    var bitmap = TextureDecoder.LoadBitmapImage(iconPath);
                    var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                    brush.Freeze();
                    OriginalIconFill = brush;
                    IconFill = brush;
                    IconStatusText = "";
                }
                catch
                {
                    OriginalIconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));
                    IconFill = OriginalIconFill;
                    IconStatusText = "Icon error";
                }
            }
            else
            {
                OriginalIconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));
                IconFill = OriginalIconFill;
                IconStatusText = "No icon";
            }
        }

        private async void ExecuteEdit(object? parameter)
        {
            if (SelectedBackup != null && SelectedBackup.FullPath != null)
            {
                var selected = SelectedBackup; // capture instance for thread

                bool isLocked = selected.IsLocked ?? false;
                bool isSubLevel = selected.IsSubLevel ?? false;
                bool isShareable = selected.IsShareable ?? true;

                if (!selected.IsLocked.HasValue)
                {
                    StatusText = "Reading slot data...";
                    _isBusy = true;
                    CommandManager.InvalidateRequerySuggested();

                    try
                    {
                        await Task.Run(() => {
                            var (_, _, _, sltHash, hashes) = Far4Archive.ReadSaveArchive(selected.FullPath);
                            string oldHashHex = Convert.ToHexStringLower(sltHash);
                            if (hashes.TryGetValue(oldHashHex, out byte[]? sltData)) {
                                sltData = SltbProcessor.DecompressSltData(sltData);
                                (isLocked, isSubLevel, isShareable) = SltbProcessor.ReadSlotBools(sltData!);
                            }
                        });
                        
                        selected.IsLocked = isLocked;
                        selected.IsSubLevel = isSubLevel;
                        selected.IsShareable = isShareable;
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log("ExecuteEdit.ReadSlt", ex);
                    }

                    _isBusy = false;
                    CommandManager.InvalidateRequerySuggested();
                }

                var owner = Application.Current.Windows.OfType<BackupManagerWindow>().FirstOrDefault();
                var dialog = new EditInfoDialog(selected.LevelName ?? "", selected.Description ?? "", selected.IconPath, isLocked, isSubLevel, isShareable)
                {
                    Owner = owner
                };

                if (dialog.ShowDialog() == true)
                {
                    string newName = dialog.LevelName;
                    string newDesc = dialog.Description;
                    string? newIcon = dialog.NewIconPath;
                    bool newLocked = dialog.IsLocked;
                    bool newSubLevel = dialog.IsSubLevel;
                    bool newShareable = dialog.IsShareable;

                    if (newName == selected.LevelName && newDesc == selected.Description && newIcon == null && newLocked == isLocked && newSubLevel == isSubLevel && newShareable == isShareable) return;

                    StatusText = "Updating and re-encrypting backup...";
                    _isBusy = true;
                    CommandManager.InvalidateRequerySuggested();

                    try
                    {
                        var backupToUpdate = selected; // capture instance for thread
                        await Task.Run(() => SaveDataBuilder.UpdateLevelInfo(backupToUpdate.FullPath, newName, newDesc, newIcon, newLocked, newSubLevel, newShareable));

                        backupToUpdate.LevelName = newName;
                        backupToUpdate.Description = newDesc;
                        backupToUpdate.IsLocked = newLocked;
                        backupToUpdate.IsSubLevel = newSubLevel;
                        backupToUpdate.IsShareable = newShareable;
                        
                        // Force UI refresh
                        UpdateSelectionDetails();
                        if (newIcon != null) LoadIconPreview(backupToUpdate.IconPath);

                        StatusText = "Level info updated successfully!";
                    }
                    catch (Exception ex)
                    {
                        _viewService.Alert($"Failed to update info:\n{ex.Message}", "Error");
                        StatusText = "Update failed.";
                    }
                    finally
                    {
                        _isBusy = false;
                        CommandManager.InvalidateRequerySuggested();

                        if (!string.IsNullOrEmpty(newIcon) && newIcon.Contains(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(newIcon); } catch { }
                        }
                    }
                }
            }
        }

        private void ExecuteDelete(object? parameter)
        {
            if (parameter is IList items && items.Count > 0)
            {
                var selectedItems = items.Cast<BackupItemViewModel>().ToList();

                bool isConfirmed = _viewService.Confirm(
                    $"Are you sure you want to permanently delete {selectedItems.Count} backup(s)?",
                    "Confirm Deletion");

                if (isConfirmed)
                {
                    int fallbackIndex = selectedItems.Select(item => BackupList.IndexOf(item)).DefaultIfEmpty(-1).Min();
                    int deletedCount = 0;

                    SelectedBackup = null;

                    foreach (var item in selectedItems)
                    {
                        try
                        {
                            if (item.FullPath != null)
                            {
                                string resolvedPath = Path.GetFullPath(item.FullPath);
                                string resolvedBackupDir = Path.GetFullPath(_backupDir);
                                string separator = Path.DirectorySeparatorChar.ToString();

                                if (!resolvedBackupDir.EndsWith(separator)) resolvedBackupDir += separator;
                                if (!resolvedPath.StartsWith(resolvedBackupDir, StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidOperationException("Path traversal detected.");

                                Directory.Delete(resolvedPath, true);
                            }

                            BackupList.Remove(item);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            _viewService.Alert($"Failed to delete {item.FolderName}.\nError: {ex.Message}", "Error");
                        }
                    }

                    StatusText = $"Deleted {deletedCount} backup(s).";

                    if (BackupList.Any())
                    {
                        if (fallbackIndex < 0) fallbackIndex = 0;
                        if (fallbackIndex >= BackupList.Count) fallbackIndex = BackupList.Count - 1;
                        SelectedBackup = BackupList[fallbackIndex];
                    }
                }
            }
        }

        private void ExecuteMove(object? parameter)
        {
            if (parameter is IList items && items.Count > 0)
            {
                var selectedItems = items.Cast<BackupItemViewModel>().ToList();
                var dialog = new OpenFolderDialog { Title = "Select Destination Folder" };

                if (dialog.ShowDialog() == true)
                {
                    string destDir = dialog.FolderName;
                    int movedCount = 0;
                    
                    SelectedBackup = null;

                    foreach (var item in selectedItems)
                    {
                        try
                        {
                            if (item.FullPath != null)
                            {
                                string sourcePath = item.FullPath;
                                string destPath = Path.Combine(destDir, item.FolderName ?? Path.GetFileName(sourcePath));

                                if (Directory.Exists(destPath))
                                {
                                    _viewService.Alert($"Destination already contains a folder named {item.FolderName}.", "Skip");
                                    continue;
                                }

                                MoveDirectoryRobust(sourcePath, destPath);
                                BackupList.Remove(item);
                                movedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _viewService.Alert($"Failed to move {item.FolderName}.\nError: {ex.Message}", "Error");
                        }
                    }

                    StatusText = $"Moved {movedCount} backup(s).";
                    if (movedCount > 0) _viewService.Alert($"Successfully moved {movedCount} backup(s) to the new location.", "Move Successful");
                    if (BackupList.Any()) SelectedBackup = BackupList[0];
                }
            }
        }

        private void ExecuteViewTextures(object? parameter)
        {
            if (SelectedBackup != null && SelectedBackup.FullPath != null)
            {
                var owner = Application.Current.Windows.OfType<BackupManagerWindow>().FirstOrDefault();
                var dialog = new TextureViewerDialog(SelectedBackup.FullPath, SelectedBackup.LevelName ?? "Level")
                {
                    Owner = owner
                };
                dialog.ShowDialog();
            }
        }

        private void ExecuteChangeDir(object? parameter)
        {
            var dialog = new OpenFolderDialog { Title = "Select Backup Directory" };

            if (dialog.ShowDialog() == true)
            {
                _backupDir = dialog.FolderName;
                ConfigManager.BackupDirectory = _backupDir;
                _ = ConfigManager.SaveConfigAsync();
                LoadBackups();
            }
        }

        private void MoveDirectoryRobust(string sourceDir, string destDir)
        {
            string sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourceDir)) ?? "";
            string destRoot = Path.GetPathRoot(Path.GetFullPath(destDir)) ?? "";

            if (string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(sourceDir, destDir);
            }
            else
            {
                CopyDirectoryRecursively(sourceDir, destDir);
                Directory.Delete(sourceDir, true);
            }
        }

        private void CopyDirectoryRecursively(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(sourceDir)) File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(sourceDir)) CopyDirectoryRecursively(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }

        private Brush GetBrush(string resourceKey, Color fallback)
        {
            if (Application.Current.TryFindResource(resourceKey) is Brush resourceBrush)
            {
                return resourceBrush;
            }
            var brush = new SolidColorBrush(fallback);
            brush.Freeze();
            return brush;
        }
    }
}