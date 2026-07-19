using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace LbpArchiveToolkit.ViewModels
{
    public class HeartedLevelsWindowViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;
        private CancellationTokenSource? _iconCts;
        private long _currentIconRequestId = -1;

        public ObservableCollection<LevelItem> HeartedList { get; } = new();

        private LevelItem? _selectedLevel;
        public LevelItem? SelectedLevel
        {
            get => _selectedLevel;
            set { if (SetProperty(ref _selectedLevel, value)) UpdateSelectionDetails(); }
        }

        private string _statusText = "Ready.";
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        private string _levelTitle = "";
        public string LevelTitle { get => _levelTitle; set => SetProperty(ref _levelTitle, value); }

        private string _levelDescription = "";
        public string LevelDescription { get => _levelDescription; set => SetProperty(ref _levelDescription, value); }

        private string _levelCreator = "";
        public string LevelCreator { get => _levelCreator; set => SetProperty(ref _levelCreator, value); }

        private Visibility _heartOverlayVisibility = Visibility.Hidden;
        public Visibility HeartOverlayVisibility { get => _heartOverlayVisibility; set => SetProperty(ref _heartOverlayVisibility, value); }

        private Visibility _mmPickVisibility = Visibility.Hidden;
        public Visibility MmPickVisibility { get => _mmPickVisibility; set => SetProperty(ref _mmPickVisibility, value); }

        private Brush _iconStroke;
        public Brush IconStroke { get => _iconStroke; set => SetProperty(ref _iconStroke, value); }

        private Brush _iconFill;
        public Brush IconFill { get => _iconFill; set => SetProperty(ref _iconFill, value); }

        private string _iconStatusText = "Select a level\nto view details";
        public string IconStatusText { get => _iconStatusText; set => SetProperty(ref _iconStatusText, value); }

        public ICommand RemoveCommand { get; }
        public ICommand ExtractCommand { get; }

        public HeartedLevelsWindowViewModel(IViewService viewService)
        {
            _viewService = viewService;

            // Initialize default brushes
            _iconStroke = GetBrush("LbpOrange", Color.FromRgb(255, 183, 3));
            _iconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));

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
            foreach (var item in HeartedLevelsManager.HeartedLevels)
            {
                HeartedList.Add(item);
            }
            StatusText = $"You have {HeartedList.Count} hearted level(s).";
            HeartOverlayVisibility = Visibility.Hidden;

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
                LevelTitle = selected.LevelName ?? "";
                LevelDescription = selected.Description ?? "";
                LevelCreator = $"By: {selected.Creator}  |  Game: {selected.Game}";
                HeartOverlayVisibility = Visibility.Visible;

                MmPickVisibility = selected.IsMmPick ? Visibility.Visible : Visibility.Hidden;
                IconStroke = selected.IsMmPick ? GetBrush("LbpPink", Color.FromRgb(247, 37, 133)) : GetBrush("LbpOrange", Color.FromRgb(255, 183, 3));

                long expectedRequestId = Interlocked.Increment(ref _currentIconRequestId);
                _iconCts?.Cancel();
                _iconCts = new CancellationTokenSource();

                await LoadIconAsync(selected.IconHash, _iconCts.Token, expectedRequestId);
            }
            else
            {
                LevelTitle = "";
                LevelDescription = "";
                LevelCreator = "";
                HeartOverlayVisibility = Visibility.Hidden;
                MmPickVisibility = Visibility.Hidden;
                IconStroke = GetBrush("LbpOrange", Color.FromRgb(255, 183, 3));
                IconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));
                IconStatusText = "Select a level\nto view details";
            }
        }

        private async Task LoadIconAsync(string? hash, CancellationToken token, long expectedRequestId)
        {
            IconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));

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
                IconFill = brush;
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
                
                // Get the current window to act as the extraction progress popup owner
                var ownerWindow = Application.Current.Windows.OfType<HeartedLevelsWindow>().FirstOrDefault() ?? _viewService.GetMainWindow();
                
                await LevelExtractionService.ExtractLevelsAsync(ownerWindow, selectedItems, lvl =>
                {
                    lvl.Saved = "✓";
                });

                StatusText = "Extraction finished.";
            }
        }

        private Brush GetBrush(string resourceKey, Color fallback)
        {
            return Application.Current.TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);
        }
    }
}