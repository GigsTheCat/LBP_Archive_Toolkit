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
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class PlaylistsWindowViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;

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
                    InvalidateCommands();
                }
            }
        }

        public string PlaylistTitle => SelectedPlaylist != null ? SelectedPlaylist.Name : "NO PLAYLIST SELECTED";

        public BulkObservableCollection<LevelItem> LevelsInPlaylist { get; } = new();

        public LevelItem? SelectedLevel
        {
            get;
            set { if (SetProperty(ref field, value)) { UpdateSelectionDetails(); InvalidateCommands(); } }
        }

        private void InvalidateCommands()
        {
            (RenameCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DownloadAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ZipAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveLevelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExtractLevelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private CancellationTokenSource? _iconCts;
        private long _currentIconRequestId = -1;

        public string LevelTitle { get; set => SetProperty(ref field, value); } = "";
        public string LevelCreator { get; set => SetProperty(ref field, value); } = "";
        public string LevelDescription { get; set => SetProperty(ref field, value); } = "";
        public object? IconSource { get; set => SetProperty(ref field, value); }
        public string IconStatusText { get; set => SetProperty(ref field, value); } = "Select a level\nto view details";
        public bool IsIconLockVisible { get; set => SetProperty(ref field, value); }
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

        public PlaylistsWindowViewModel(IViewService viewService) : base(viewService)
        {
            _viewService = viewService;

            CreateCommand = new RelayCommand(_ => ExecuteCreate());
            RenameCommand = new RelayCommand(_ => ExecuteRename(), _ => SelectedPlaylist != null);
            DeleteCommand = new RelayCommand(_ => ExecuteDelete(), _ => SelectedPlaylist != null);
            ExportCommand = new RelayCommand(_ => ExecuteExport(), _ => SelectedPlaylist != null);
            ImportCommand = new RelayCommand(_ => ExecuteImport());
            DownloadAllCommand = new RelayCommand(_ => ExecuteDownloadAll(), _ => SelectedPlaylist != null && SelectedPlaylist.Levels.Count > 0);
            ZipAllCommand = new RelayCommand(_ => ExecuteZipAll(), _ => SelectedPlaylist != null && SelectedPlaylist.Levels.Count > 0);
            RemoveLevelCommand = new RelayCommand(ExecuteRemoveLevel, CanExecuteRemoveLevel);
            ExtractLevelCommand = new RelayCommand(ExecuteExtractLevel, CanExecuteRemoveLevel);

            LoadPlaylists();
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
                
                IsIconLockVisible = selected.IsLocked;
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
                IsIconLockVisible = false;
                IconScale = 1.0;
                IconSource = null;
                IconStatusText = "Select a level\nto view details";
            }
        }

        private async Task LoadIconAsync(string? hash, CancellationToken token, long expectedRequestId, bool isLocked)
        {
            IconSource = null;

            if (string.IsNullOrEmpty(hash) || hash.Length <= 8)
            {
                IconStatusText = "No Icon Available";
                return;
            }

            IconStatusText = "Loading Icon...";

            var bmp = await IconLoaderService.LoadIconSourceAsync(hash, MainWindow.SharedHttpClient, _viewService, token);

            if (_currentIconRequestId != expectedRequestId || token.IsCancellationRequested) return;

            if (bmp != null)
            {
                IconSource = bmp;
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
            if (_viewService.ShowInputDialog("Enter a name for the new playlist:", "New Playlist", "New Playlist", out string newName))
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
                if (_viewService.ShowInputDialog("Enter a new name for the playlist:", "Rename Playlist", SelectedPlaylist.Name, out string newName))
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
                    using (var brotli = new BrotliStream(ms, CompressionLevel.SmallestSize, true))
                    using (var writer = new BinaryWriter(brotli, System.Text.Encoding.UTF8))
                    {
                        writer.Write((byte)1); // Version Header
                        writer.Write(SelectedPlaylist.Name ?? "My Playlist");
                        writer.Write(SelectedPlaylist.Levels.Count);
                        foreach (var lvl in SelectedPlaylist.Levels)
                        {
                            writer.Write7BitEncodedInt64(lvl.Id);
                        }
                    }
                    
                    string base64 = Convert.ToBase64String(ms.ToArray());
                    string code = "LBP-" + base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
                    _viewService.SetClipboardText(code);
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
            if (_viewService.ShowInputDialog("Paste a Playlist Share Code here:", "Import Playlist", "", out string code))
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
                    using var brotli = new BrotliStream(ms, CompressionMode.Decompress);
                    using var reader = new BinaryReader(brotli, System.Text.Encoding.UTF8);

                    byte version = reader.ReadByte();
                    if (version != 1) throw new Exception("Unsupported share code version.");

                    string name = reader.ReadString();
                    int count = reader.ReadInt32();
                    
                    var newPlaylist = new Playlist { Id = Guid.NewGuid().ToString(), Name = name };
                    var dbService = new DatabaseService(ConfigManager.DatabasePath);
                    
                    var fetchedLevels = new List<LevelItem>();
                    for (int i = 0; i < count; i++)
                    {
                        long id = reader.Read7BitEncodedInt64();
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
                    await LevelExtractionService.ExtractLevelsAsync(SelectedPlaylist.Levels.ToList(), _viewService);
                }
            }
        }

        private async void ExecuteZipAll()
        {
            if (SelectedPlaylist != null && SelectedPlaylist.Levels.Any())
            {
                string? fileName = _viewService.ShowSaveFileDialog("ZIP Archive (*.zip)|*.zip", "Download and Zip Playlist", SelectedPlaylist.Name + ".zip");

                if (fileName != null)
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "LbpArchiveToolkit_Zip_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);

                    try
                    {
                        bool originalPrompt = ConfigManager.ShowExtractionSuccessPrompt;
                        ConfigManager.ShowExtractionSuccessPrompt = false;

                        await LevelExtractionService.ExtractLevelsAsync(SelectedPlaylist.Levels.ToList(), _viewService, null, tempDir);

                        ConfigManager.ShowExtractionSuccessPrompt = originalPrompt;

                        if (Directory.GetDirectories(tempDir).Length > 0)
                        {
                            if (File.Exists(fileName)) File.Delete(fileName);
                            ZipFile.CreateFromDirectory(tempDir, fileName, CompressionLevel.Optimal, false);
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
                        
                    InvalidateCommands();
                }
            }
        }

        private async void ExecuteExtractLevel(object? parameter)
        {
            if (parameter is IList items && items.Count > 0)
            {
                var selectedItems = items.Cast<LevelItem>().ToList();
                await LevelExtractionService.ExtractLevelsAsync(selectedItems, _viewService);
            }
        }
    }
}