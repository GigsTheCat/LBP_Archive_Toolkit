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

        public BulkObservableCollection<Playlist> Playlists { get; } = new();

        public Playlist? SelectedPlaylist
        {
            get;
            set
            {
                if (SetProperty(ref field, value))
                {
                    RefreshLevelsList();
                    OnPropertyChanged(nameof(PlaylistTitle));
                }
            }
        }

        public string PlaylistTitle => SelectedPlaylist != null ? SelectedPlaylist.Name : "NO PLAYLIST SELECTED";

        public BulkObservableCollection<LevelItem> LevelsInPlaylist { get; } = new();

        public LevelItem? SelectedLevel
        {
            get;
            set { if (SetProperty(ref field, value)) UpdateSelectionDetails(); }
        }

        private CancellationTokenSource? _iconCts;
        private long _currentIconRequestId = -1;

        public string LevelTitle { get; set => SetProperty(ref field, value); } = "";
        public string LevelCreator { get; set => SetProperty(ref field, value); } = "";
        public string LevelDescription { get; set => SetProperty(ref field, value); } = "";
        public Brush IconFill { get; set => SetProperty(ref field, value); } = CreateFrozenBrush(Color.FromRgb(25, 19, 43));
        public Brush OriginalIconFill { get; set => SetProperty(ref field, value); } = CreateFrozenBrush(Color.FromRgb(25, 19, 43));

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        public string IconStatusText { get; set => SetProperty(ref field, value); } = "Select a level\nto view details";
        public Visibility IconLockVisibility { get; set => SetProperty(ref field, value); } = Visibility.Hidden;
        public double IconScale { get; set => SetProperty(ref field, value); } = 1.0;

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

            IconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));
            OriginalIconFill = IconFill;

            LoadPlaylists();
        }

        private Brush GetBrush(string resourceKey, Color fallback)
        {
            if (Application.Current.TryFindResource(resourceKey) is Brush resourceBrush)
            {
                return resourceBrush;
            }
            return CreateFrozenBrush(fallback);
        }

        private async void UpdateSelectionDetails()
        {
            var selected = SelectedLevel;
            if (selected != null)
            {
                if (selected.Hash == null || selected.Description == null)
                {
                    var dbService = new DatabaseService(ConfigManager.DatabasePath);
                    await dbService.FetchLevelDetailsAsync(selected);
                    PlaylistsManager.Save();
                }

                LevelTitle = selected.LevelName ?? "Unnamed Level";
                LevelCreator = $"By: {selected.Creator ?? "Unknown"}  |  Game: {selected.Game ?? "Unknown"}";
                LevelDescription = selected.Description ?? "No description provided.";
                
                IconLockVisibility = selected.IsLocked ? Visibility.Visible : Visibility.Hidden;
                IconScale = selected.IsSubLevel ? 0.85 : 1.0;

                long expectedRequestId = Interlocked.Increment(ref _currentIconRequestId);
                if (_iconCts != null)
                {
                    _iconCts.Cancel();
                    _iconCts.Dispose();
                }
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
            Playlists.AddRange(PlaylistsManager.Playlists);

            if (Playlists.Any())
                SelectedPlaylist = Playlists[0];
        }

        private void RefreshLevelsList()
        {
            LevelsInPlaylist.Clear();
            if (SelectedPlaylist != null)
            {
                LevelsInPlaylist.AddRange(SelectedPlaylist.Levels);
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
                try
                {
                    using var ms = new MemoryStream();
                    using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, true))
                    using (var writer = new BinaryWriter(deflate, System.Text.Encoding.UTF8))
                    {
                        writer.Write((byte)1); // Version Header
                        writer.Write(SelectedPlaylist.Name ?? "My Playlist");
                        writer.Write(SelectedPlaylist.Levels.Count);
                        foreach (var lvl in SelectedPlaylist.Levels)
                        {
                            writer.Write(lvl.Id);
                        }
                    }
                    
                    string base64 = Convert.ToBase64String(ms.ToArray());
                    string code = "LBP-" + base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
                    Clipboard.SetText(code);
                    _viewService.Alert("Share Code generated and copied to clipboard!", "Export Complete");
                }
                catch (Exception ex)
                {
                    _viewService.Alert($"Failed to generate share code: {ex.Message}", "Error");
                }
            }
        }

        private async void ExecuteImport()
        {
            if (CustomDialog.ShowInput(_ownerWindow, "Paste a Playlist Share Code here:", "Import Playlist", "", out string code))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(code)) return;
                    if (code.StartsWith("LBP-")) code = code.Substring(4);
                    code = code.Replace("-", "+").Replace("_", "/");
                    switch (code.Length % 4)
                    {
                        case 2: code += "=="; break;
                        case 3: code += "="; break;
                    }

                    byte[] bytes = Convert.FromBase64String(code);
                    
                    using var ms = new MemoryStream(bytes);
                    using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
                    using var reader = new BinaryReader(deflate, System.Text.Encoding.UTF8);

                    byte version = reader.ReadByte();
                    if (version != 1) throw new Exception("Unsupported share code version.");

                    string name = reader.ReadString();
                    int count = reader.ReadInt32();
                    
                    var newPlaylist = new Playlist { Id = Guid.NewGuid().ToString(), Name = name };
                    var dbService = new DatabaseService(ConfigManager.DatabasePath);
                    
                    var fetchedLevels = new List<LevelItem>();
                    for (int i = 0; i < count; i++)
                    {
                        long id = reader.ReadInt64();
                        var results = new List<LevelItem>();
                        await foreach (var lvl in dbService.SearchLevelsAsync(id.ToString(), false, false, 0, "All Genres", "1", new HashSet<long>(), new HashSet<long>(), new HashSet<long>(), new AdvancedSearchCriteria(), null, false, false, true, false, false, CancellationToken.None))
                        {
                            results.Add(lvl);
                        }
                        
                        if (results.Count > 0)
                        {
                            fetchedLevels.Add(results[0]);
                        }
                    }

                    if (fetchedLevels.Count == 0)
                    {
                        _viewService.Alert("No valid levels could be found in the database for this share code.", "Import Failed");
                        return;
                    }

                    newPlaylist.Levels.AddRange(fetchedLevels);
                    PlaylistsManager.AddPlaylist(newPlaylist);
                    Playlists.Add(newPlaylist);
                    SelectedPlaylist = newPlaylist;
                    
                    _viewService.Alert($"Successfully imported playlist '{newPlaylist.Name}' with {fetchedLevels.Count} level(s)!", "Success");
                }
                catch (Exception ex)
                {
                    _viewService.Alert($"Failed to import share code. The code may be invalid or corrupted.\nError: {ex.Message}", "Error");
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