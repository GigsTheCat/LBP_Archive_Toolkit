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

        public BulkObservableCollection<LevelItem> HeartedList { get; } = new();

        public LevelItem? SelectedLevel
        {
            get;
            set { if (SetProperty(ref field, value)) UpdateSelectionDetails(); }
        }

        public string StatusText { get; set => SetProperty(ref field, value); } = "Ready.";
        public string LevelTitle { get; set => SetProperty(ref field, value); } = "";
        public string LevelDescription { get; set => SetProperty(ref field, value); } = "";
        public string LevelCreator { get; set => SetProperty(ref field, value); } = "";
        public Visibility HeartOverlayVisibility { get; set => SetProperty(ref field, value); } = Visibility.Hidden;
        public Visibility MmPickVisibility { get; set => SetProperty(ref field, value); } = Visibility.Hidden;
        public Brush IconStroke { get; set => SetProperty(ref field, value); } = null!;
        public Brush IconFill { get; set => SetProperty(ref field, value); } = null!;
        public Brush OriginalIconFill { get; set => SetProperty(ref field, value); } = null!;
        public Visibility IconLockVisibility { get; set => SetProperty(ref field, value); } = Visibility.Hidden;
        public double IconScale { get; set => SetProperty(ref field, value); } = 1.0;
        public string IconStatusText { get; set => SetProperty(ref field, value); } = "Select a level\nto view details";

        public ICommand RemoveCommand { get; }
        public ICommand ExtractCommand { get; }

        public HeartedLevelsWindowViewModel(IViewService viewService)
        {
            _viewService = viewService;

            // Initialize default brushes
            IconStroke = GetBrush("LbpOrange", Color.FromRgb(255, 183, 3));
            IconFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));
            OriginalIconFill = IconFill;

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
                LevelDescription = "";
                LevelCreator = "";
                HeartOverlayVisibility = Visibility.Hidden;
                MmPickVisibility = Visibility.Hidden;
                IconLockVisibility = Visibility.Hidden;
                IconScale = 1.0;
                IconStroke = GetBrush("LbpOrange", Color.FromRgb(255, 183, 3));
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