using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using System;
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
    public class HeartedCreatorsWindowViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;
        private CancellationTokenSource? _iconCts;
        private long _currentIconRequestId = -1;

        public ObservableCollection<UserItem> HeartedList { get; } = new();

        private UserItem? _selectedUser;
        public UserItem? SelectedUser
        {
            get => _selectedUser;
            set { if (SetProperty(ref _selectedUser, value)) UpdateSelectionDetails(); }
        }

        private string _statusText = "Ready.";
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        private string _userNpHandle = "";
        public string UserNpHandle { get => _userNpHandle; set => SetProperty(ref _userNpHandle, value); }

        private string _userStats = "";
        public string UserStats { get => _userStats; set => SetProperty(ref _userStats, value); }

        private string _userSummary = "";
        public string UserSummary { get => _userSummary; set => SetProperty(ref _userSummary, value); }

        private Visibility _heartOverlayVisibility = Visibility.Hidden;
        public Visibility HeartOverlayVisibility { get => _heartOverlayVisibility; set => SetProperty(ref _heartOverlayVisibility, value); }

        private Brush _iconRectFill;
        public Brush IconRectFill { get => _iconRectFill; set => SetProperty(ref _iconRectFill, value); }

        private string _iconStatusText = "Select a creator\nto view details";
        public string IconStatusText { get => _iconStatusText; set => SetProperty(ref _iconStatusText, value); }

        private Visibility _viewContributionsVisibility = Visibility.Collapsed;
        public Visibility ViewContributionsVisibility { get => _viewContributionsVisibility; set => SetProperty(ref _viewContributionsVisibility, value); }

        private Visibility _viewObjectsVisibility = Visibility.Collapsed;
        public Visibility ViewObjectsVisibility { get => _viewObjectsVisibility; set => SetProperty(ref _viewObjectsVisibility, value); }

        public ICommand RemoveCommand { get; }
        public ICommand ViewUserLevelsCommand { get; }
        public ICommand ViewUserContributionsCommand { get; }
        public ICommand ViewUserObjectsCommand { get; }
        public ICommand DownloadAllLevelsCommand { get; }

        public Action? RequestClose;

        public HeartedCreatorsWindowViewModel(IViewService viewService)
        {
            _viewService = viewService;

            // Initialize default brush
            _iconRectFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));

            RemoveCommand = new RelayCommand(ExecuteRemove, CanExecuteAction);
            ViewUserLevelsCommand = new RelayCommand(ExecuteViewUserLevels, CanExecuteAction);
            ViewUserContributionsCommand = new RelayCommand(ExecuteViewUserContributions, CanExecuteAction);
            ViewUserObjectsCommand = new RelayCommand(ExecuteViewUserObjects, CanExecuteAction);
            DownloadAllLevelsCommand = new RelayCommand(ExecuteDownloadAllLevels, CanExecuteAction);

            // Access owner window to determine available database features
            if (_viewService.GetMainWindow() is MainWindow mainWindow)
            {
                ViewContributionsVisibility = mainWindow.HasContributorsTable ? Visibility.Visible : Visibility.Collapsed;
                ViewObjectsVisibility = mainWindow.HasObjectContributorsTable ? Visibility.Visible : Visibility.Collapsed;
            }

            LoadHeartedCreators();
        }

        private bool CanExecuteAction(object? parameter)
        {
            return parameter is IList items && items.Count > 0;
        }

        private void LoadHeartedCreators()
        {
            HeartedList.Clear();
            foreach (var item in HeartedCreatorsManager.HeartedCreators)
            {
                HeartedList.Add(item);
            }
            
            StatusText = $"You have {HeartedList.Count} hearted creator(s).";
            HeartOverlayVisibility = Visibility.Hidden;

            if (HeartedList.Any())
            {
                SelectedUser = HeartedList[0];
            }
        }

        private async void UpdateSelectionDetails()
        {
            var selected = SelectedUser;
            if (selected != null)
            {
                UserNpHandle = selected.NpHandle;
                UserStats = $"Hearts: {selected.HeartCount}  |  Total Levels: {selected.TotalLevels}";
                UserSummary = $"Published Level slots summary:\n" +
                              $"• LBP1 Slots: {selected.Lbp1UsedSlots}\n" +
                              $"• LBP2 Slots: {selected.Lbp2UsedSlots}\n" +
                              $"• LBP3 Slots: {selected.Lbp3UsedSlots}";
                HeartOverlayVisibility = Visibility.Visible;

                long expectedRequestId = Interlocked.Increment(ref _currentIconRequestId);
                _iconCts?.Cancel();
                _iconCts = new CancellationTokenSource();

                await LoadUserIconAsync(selected.IconHash, _iconCts.Token, expectedRequestId);
            }
            else
            {
                UserNpHandle = "";
                UserStats = "";
                UserSummary = "";
                HeartOverlayVisibility = Visibility.Hidden;
                IconRectFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));
                IconStatusText = "Select a creator\nto view details";
            }
        }

        private async Task LoadUserIconAsync(string? hash, CancellationToken token, long expectedRequestId)
        {
            IconRectFill = GetBrush("BgPrimary", Color.FromRgb(25, 19, 43));

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
                IconRectFill = brush;
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
                var selectedItems = items.Cast<UserItem>().ToList();
                bool isConfirmed = _viewService.Confirm(
                    $"Are you sure you want to remove {selectedItems.Count} creator(s) from your hearted list?",
                    "Confirm Removal");

                if (isConfirmed)
                {
                    // Unselect cleanly before mutating the collection
                    SelectedUser = null;

                    foreach (var item in selectedItems)
                    {
                        HeartedCreatorsManager.Remove(item.NpHandle);
                        HeartedList.Remove(item);
                    }
                    
                    StatusText = $"Removed {selectedItems.Count} creator(s).";

                    if (HeartedList.Any())
                    {
                        SelectedUser = HeartedList[0];
                    }
                }
            }
        }

        private void ExecuteViewUserLevels(object? parameter)
        {
            if (SelectedUser != null && _viewService.GetMainWindow() is MainWindow mainWindow)
            {
                RequestClose?.Invoke();
                mainWindow.InitiateCreatorSearch(SelectedUser.NpHandle);
            }
        }

        private void ExecuteViewUserContributions(object? parameter)
        {
            if (SelectedUser != null && _viewService.GetMainWindow() is MainWindow mainWindow)
            {
                RequestClose?.Invoke();
                mainWindow.InitiateContributionsSearch(SelectedUser.NpHandle);
            }
        }

        private void ExecuteViewUserObjects(object? parameter)
        {
            if (SelectedUser != null && _viewService.GetMainWindow() is MainWindow mainWindow)
            {
                RequestClose?.Invoke();
                mainWindow.InitiateObjectsSearch(SelectedUser.NpHandle);
            }
        }

        private async void ExecuteDownloadAllLevels(object? parameter)
        {
            if (SelectedUser != null && _viewService.GetMainWindow() is MainWindow mainWindow)
            {
                RequestClose?.Invoke();
                await mainWindow.InitiateBatchDownloadAsync(SelectedUser);
            }
        }

        private Brush GetBrush(string resourceKey, Color fallback)
        {
            return Application.Current.TryFindResource(resourceKey) as Brush ?? new SolidColorBrush(fallback);
        }
    }
}