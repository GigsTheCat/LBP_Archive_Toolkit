using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class HeartedLevelsWindowViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;
        private CancellationTokenSource? _iconCts;
        private long _currentIconRequestId = -1;

        public BulkObservableCollection<LevelItem> HeartedList { get; } = new();

        public LevelItem? SelectedLevel
        {
            get;
            set { if (SetProperty(ref field, value)) { UpdateSelectionDetails(); InvalidateCommands(); } }
        }

        private void InvalidateCommands()
        {
            (RemoveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExtractCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        public string StatusText { get; set => SetProperty(ref field, value); } = "Ready.";
        public string LevelTitle { get; set => SetProperty(ref field, value); } = "";
        public string LevelDescription { get; set => SetProperty(ref field, value); } = "";
        public string LevelCreator { get; set => SetProperty(ref field, value); } = "";
        public bool IsHeartOverlayVisible { get; set => SetProperty(ref field, value); }
        public bool IsMmPickVisible { get; set => SetProperty(ref field, value); }
        public object? IconSource { get; set => SetProperty(ref field, value); }
        public bool IsIconLockVisible { get; set => SetProperty(ref field, value); }
        public double IconScale { get; set => SetProperty(ref field, value); } = 1.0;
        public string IconStatusText { get; set => SetProperty(ref field, value); } = "Select a level\nto view details";

        public ICommand RemoveCommand { get; }
        public ICommand ExtractCommand { get; }

        public HeartedLevelsWindowViewModel(IViewService viewService) : base(viewService)
        {
            _viewService = viewService;

            RemoveCommand = new RelayCommand(ExecuteRemove, CanExecuteAction);
            ExtractCommand = new RelayCommand(ExecuteExtract, CanExecuteAction);

            LoadHeartedLevels();
        }

        private bool CanExecuteAction(object? parameter)
        {
            return parameter is IList items && items.Count > 0;
        }

        private void LoadHeartedLevels()
        {
            HeartedList.Clear();
            HeartedList.AddRange(HeartedLevelsManager.HeartedLevels);
            
            StatusText = $"You have {HeartedList.Count} hearted level(s).";
            IsHeartOverlayVisible = false;

            if (HeartedList.Any())
            {
                SelectedLevel = HeartedList[0];
            }
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
                    HeartedLevelsManager.Save();
                }

                LevelTitle = selected.LevelName ?? "";
                LevelDescription = selected.Description ?? "";
                LevelCreator = $"By: {selected.Creator}  |  Game: {selected.Game}";
                IsHeartOverlayVisible = true;

                IsMmPickVisible = selected.IsMmPick;

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
                LevelDescription = "";
                LevelCreator = "";
                IsHeartOverlayVisible = false;
                IsMmPickVisible = false;
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

        private void ExecuteRemove(object? parameter)
        {
            if (parameter is IList items && items.Count > 0)
            {
                var selectedItems = items.Cast<LevelItem>().ToList();
                bool isConfirmed = _viewService.Confirm(
                    $"Are you sure you want to remove {selectedItems.Count} level(s) from your hearted list?",
                    "Confirm Removal");

                if (isConfirmed)
                {
                    // Clean deselect to avoid out-of-bounds errors while modifying the collection
                    SelectedLevel = null;

                    foreach (var item in selectedItems)
                    {
                        HeartedLevelsManager.Remove(item.Id);
                        HeartedList.Remove(item);
                    }
                    
                    StatusText = $"Removed {selectedItems.Count} level(s).";

                    if (HeartedList.Any())
                    {
                        SelectedLevel = HeartedList[0];
                    }
                }
            }
        }

        private async void ExecuteExtract(object? parameter)
        {
            if (parameter is IList items && items.Count > 0)
            {
                var selectedItems = items.Cast<LevelItem>().ToList();
                
                await LevelExtractionService.ExtractLevelsAsync(selectedItems, _viewService, lvl =>
                {
                    lvl.Saved = "✓";
                });

                StatusText = "Extraction finished.";
            }
        }
    }
}