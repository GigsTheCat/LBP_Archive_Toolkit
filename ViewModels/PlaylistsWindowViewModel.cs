using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace LbpArchiveToolkit.ViewModels
{
    public class PlaylistsWindowViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;
        private readonly Window _ownerWindow;

        public ObservableCollection<Playlist> Playlists { get; } = new();

        private Playlist? _selectedPlaylist;
        public Playlist? SelectedPlaylist
        {
            get => _selectedPlaylist;
            set
            {
                if (SetProperty(ref _selectedPlaylist, value))
                {
                    RefreshLevelsList();
                    OnPropertyChanged(nameof(PlaylistTitle));
                }
            }
        }

        public string PlaylistTitle => SelectedPlaylist != null ? SelectedPlaylist.Name : "NO PLAYLIST SELECTED";

        public ObservableCollection<LevelItem> LevelsInPlaylist { get; } = new();

        private LevelItem? _selectedLevel;
        public LevelItem? SelectedLevel
        {
            get => _selectedLevel;
            set { if (SetProperty(ref _selectedLevel, value)) UpdateSelectionDetails(); }
        }

        private CancellationTokenSource? _iconCts;
        private long _currentIconRequestId = -1;

        private string _levelTitle = "";
        public string LevelTitle { get => _levelTitle; set => SetProperty(ref _levelTitle, value); }

        private string _levelCreator = "";
        public string LevelCreator { get => _levelCreator; set => SetProperty(ref _levelCreator, value); }

        private string _levelDescription = "";
        public string LevelDescription { get => _levelDescription; set => SetProperty(ref _levelDescription, value); }

        private Brush _iconFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
        public Brush IconFill { get => _iconFill; set => SetProperty(ref _iconFill, value); }

        private Brush _originalIconFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
        public Brush OriginalIconFill { get => _originalIconFill; set => SetProperty(ref _originalIconFill, value); }

        private string _iconStatusText = "Select a level\nto view details";
        public string IconStatusText { get => _iconStatusText; set => SetProperty(ref _iconStatusText, value); }

        private Visibility _iconLockVisibility = Visibility.Hidden;
        public Visibility IconLockVisibility { get => _iconLockVisibility; set => SetProperty(ref _iconLockVisibility, value); }

        private double _iconScale = 1.0;
        public double IconScale { get => _iconScale; set => SetProperty(ref _iconScale, value); }

        public ICommand CreateCommand { get; }
        public ICommand RenameCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand DownloadAllCommand { get; }
        public ICommand ZipAllCommand { get; }
        public ICommand RemoveLevelCommand { get; }
        public ICommand ExtractLevelCommand { get; }

        public PlaylistsWindowViewModel(IViewService viewService, Window ownerWindow)
        {
            _viewService = viewService;
            _ownerWindow = ownerWindow;

            CreateCommand = new RelayCommand(_ => ExecuteCreate());
            RenameCommand = new RelayCommand(_ => ExecuteRename(), _ => SelectedPlaylist != null);
            DeleteCommand = new RelayCommand(_ => ExecuteDelete(), _ => SelectedPlaylist != null);
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => SelectedPlaylist != null);
            ImportCommand = new RelayCommand(_ => ExecuteImport());
            DownloadAllCommand = new RelayCommand(_ => ExecuteDownloadAll(), _ => SelectedPlaylist != null && SelectedPlaylist.Levels.Count > 0);
            ZipAllCommand = new RelayCommand(_ => ExecuteZipAll(), _ => SelectedPlaylist != null && SelectedPlaylist.Levels.Count > 0);
            RemoveLevelCommand = new RelayCommand(ExecuteRemoveLevel, CanExecuteRemoveLevel);
            ExtractLevelCommand = new RelayCommand(ExecuteExtractLevel, CanExecuteRemoveLevel);

            _iconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));
            _originalIconFill = _iconFill;

            LoadPlaylists();
        }

        private Brush GetBrush(string resourceKey, Color fallback)
        {
            return Application.Current.TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);
        }

        private async void UpdateSelectionDetails()
        {
            var selected = SelectedLevel;
            if (selected != null)
            {
                LevelTitle = selected.LevelName ?? "Unnamed Level";
                LevelCreator = $"By: {selected.Creator ?? "Unknown"}  |  Game: {selected.Game ?? "Unknown"}";
                LevelDescription = selected.Description ?? "No description provided.";
                
                IconLockVisibility = selected.IsLocked ? Visibility.Visible : Visibility.Hidden;
                IconScale = selected.IsSubLevel ? 0.85 : 1.0;

                long expectedRequestId = Interlocked.Increment(ref _currentIconRequestId);
                _iconCts?.Cancel();
                _iconCts = new CancellationTokenSource();

                await LoadIconAsync(selected.IconHash, _iconCts.Token, expectedRequestId, selected.IsLocked);
            }
            else
            {
                LevelTitle = "";
                LevelCreator = "";
                LevelDescription = "";
                IconLockVisibility = Visibility.Hidden;
                IconScale = 1.0;
                IconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));
                OriginalIconFill = IconFill;
                IconStatusText = "Select a level\nto view details";
            }
        }

        private async Task LoadIconAsync(string? hash, CancellationToken token, long expectedRequestId, bool isLocked)
        {
            IconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));
            OriginalIconFill = IconFill;

            if (string.IsNullOrEmpty(hash) || hash.Length <= 8)
            {
                IconStatusText = "No Icon Available";
                return;
            }

            IconStatusText = "Loading Icon...";

            var brush = await IconLoaderService.LoadIconBrushAsync(hash, MainWindow.SharedHttpClient, token);

            if (_currentIconRequestId != expectedRequestId || token.IsCancellationRequested) return;

            if (brush != null)
            {
                OriginalIconFill = brush;
                if (isLocked && brush.ImageSource is System.Windows.Media.Imaging.BitmapSource bmp)
                {
                    var grayscaleBmp = new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0);
                    grayscaleBmp.Freeze();
                    var grayBrush = new ImageBrush(grayscaleBmp) { Stretch = Stretch.UniformToFill };
                    grayBrush.Freeze();
                    IconFill = grayBrush;
                }
                else IconFill = brush;
                IconStatusText = "";
            }
            else
            {
                IconStatusText = "Icon offline\nor missing.";
            }
        }

        private void LoadPlaylists()
        {
            Playlists.Clear();
            foreach (var p in PlaylistsManager.Playlists)
                Playlists.Add(p);

            if (Playlists.Any())
                SelectedPlaylist = Playlists[0];
        }

        private void RefreshLevelsList()
        {
            LevelsInPlaylist.Clear();
            if (SelectedPlaylist != null)
            {
                foreach (var l in SelectedPlaylist.Levels)
                    LevelsInPlaylist.Add(l);
            }

            if (LevelsInPlaylist.Any())
                SelectedLevel = LevelsInPlaylist[0];
            else
                SelectedLevel = null;
        }

        private void ExecuteCreate()
        {
            if (CustomDialog.ShowInput(_ownerWindow, "Enter a name for the new playlist:", "New Playlist", "New Playlist", out string newName))
            {
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    var p = new Playlist { Name = newName.Trim() };
                    PlaylistsManager.AddPlaylist(p);
                    Playlists.Add(p);
                    SelectedPlaylist = p;
                }
            }
        }

        private void ExecuteRename()
        {
            if (SelectedPlaylist != null)
            {
                if (CustomDialog.ShowInput(_ownerWindow, "Enter a new name for the playlist:", "Rename Playlist", SelectedPlaylist.Name, out string newName))
                {
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        SelectedPlaylist.Name = newName.Trim();
                        PlaylistsManager.Save();
                        OnPropertyChanged(nameof(PlaylistTitle));
                        
                        int idx = Playlists.IndexOf(SelectedPlaylist);
                        Playlists[idx] = SelectedPlaylist;
                        SelectedPlaylist = Playlists[idx];
                    }
                }
            }
        }

        private void ExecuteDelete()
        {
            if (SelectedPlaylist != null)
            {
                if (_viewService.Confirm($"Are you sure you want to delete the playlist '{SelectedPlaylist.Name}'?", "Delete Playlist"))
                {
                    PlaylistsManager.RemovePlaylist(SelectedPlaylist.Id);
                    Playlists.Remove(SelectedPlaylist);
                    SelectedPlaylist = Playlists.FirstOrDefault();
                }
            }
        }

        private void ExecuteExport()
        {
            if (SelectedPlaylist != null)
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "LBP Playlist (*.lbpplaylist)|*.lbpplaylist",
                    Title = "Export Playlist",
                    FileName = SelectedPlaylist.Name + ".lbpplaylist"
                };

                if (dlg.ShowDialog() == true)
                {
                    try
                    {
                        var options = new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = ConfigManager.ConfigJsonContext.Default };
                        string json = JsonSerializer.Serialize(SelectedPlaylist, options);
                        File.WriteAllText(dlg.FileName, json);
                        _viewService.Alert("Playlist exported successfully.", "Export Complete");
                    }
                    catch (Exception ex)
                    {
                        _viewService.Alert($"Failed to export playlist: {ex.Message}", "Error");
                    }
                }
            }
        }

        private void ExecuteImport()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "LBP Playlist (*.lbpplaylist)|*.lbpplaylist",
                Title = "Import Playlist"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(dlg.FileName);
                    var options = new JsonSerializerOptions { TypeInfoResolver = ConfigManager.ConfigJsonContext.Default };
                    var playlist = JsonSerializer.Deserialize<Playlist>(json, options);

                    if (playlist != null)
                    {
                        playlist.Id = Guid.NewGuid().ToString(); // Assign new ID to prevent conflicts
                        PlaylistsManager.AddPlaylist(playlist);
                        Playlists.Add(playlist);
                        SelectedPlaylist = playlist;
                    }
                }
                catch (Exception ex)
                {
                    _viewService.Alert($"Failed to import playlist: {ex.Message}", "Error");
                }
            }
        }

        private async void ExecuteDownloadAll()
        {
            if (SelectedPlaylist != null && SelectedPlaylist.Levels.Any())
            {
                if (_viewService.Confirm($"Download all {SelectedPlaylist.Levels.Count} levels in '{SelectedPlaylist.Name}' to your backups folder?", "Download All"))
                {
                    await LevelExtractionService.ExtractLevelsAsync(_ownerWindow, SelectedPlaylist.Levels.ToList());
                }
            }
        }

        private async void ExecuteZipAll()
        {
            if (SelectedPlaylist != null && SelectedPlaylist.Levels.Any())
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "ZIP Archive (*.zip)|*.zip",
                    Title = "Download and Zip Playlist",
                    FileName = SelectedPlaylist.Name + ".zip"
                };

                if (dlg.ShowDialog() == true)
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "LbpArchiveToolkit_Zip_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        bool originalPrompt = ConfigManager.ShowExtractionSuccessPrompt;
                        ConfigManager.ShowExtractionSuccessPrompt = false;

                        await LevelExtractionService.ExtractLevelsAsync(_ownerWindow, SelectedPlaylist.Levels.ToList(), null, tempDir);

                        ConfigManager.ShowExtractionSuccessPrompt = originalPrompt;

                        if (Directory.GetDirectories(tempDir).Length > 0)
                        {
                            if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                            ZipFile.CreateFromDirectory(tempDir, dlg.FileName, CompressionLevel.Optimal, false);
                            _viewService.Alert($"Successfully downloaded and zipped {Directory.GetDirectories(tempDir).Length} levels.", "Zip Complete");
                        }
                        else
                        {
                            _viewService.Alert("No levels were downloaded successfully.", "Zip Failed");
                        }
                    }
                    catch (Exception ex)
                    {
                        _viewService.Alert($"An error occurred during zipping: {ex.Message}", "Error");
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir))
                        {
                            try { Directory.Delete(tempDir, true); } catch { }
                        }
                    }
                }
            }
        }

        private bool CanExecuteRemoveLevel(object? parameter) => parameter is IList items && items.Count > 0;

        private void ExecuteRemoveLevel(object? parameter)
        {
            if (SelectedPlaylist != null && parameter is IList items && items.Count > 0)
            {
                var selectedItems = items.Cast<LevelItem>().ToList();
                if (_viewService.Confirm($"Remove {selectedItems.Count} level(s) from the playlist?", "Remove Levels"))
                {
                    foreach (var item in selectedItems)
                    {
                        SelectedPlaylist.Levels.Remove(item);
                        LevelsInPlaylist.Remove(item);
                    }
                    PlaylistsManager.Save();

                    if (LevelsInPlaylist.Any())
                        SelectedLevel = LevelsInPlaylist[0];
                    else
                        SelectedLevel = null;
                }
            }
        }

        private async void ExecuteExtractLevel(object? parameter)
        {
            if (parameter is IList items && items.Count > 0)
            {
                var selectedItems = items.Cast<LevelItem>().ToList();
                await LevelExtractionService.ExtractLevelsAsync(_ownerWindow, selectedItems);
            }
        }
    }
}