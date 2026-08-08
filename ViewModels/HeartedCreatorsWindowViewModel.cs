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

        public BulkObservableCollection<UserItem> HeartedList { get; } = new();

        public UserItem? SelectedUser
        {
            get;
            set { if (SetProperty(ref field, value)) UpdateSelectionDetails(); }
        }

        public string StatusText { get; set => SetProperty(ref field, value); } = "Ready.";
        public string UserNpHandle { get; set => SetProperty(ref field, value); } = "";
        public string UserStats { get; set => SetProperty(ref field, value); } = "";
        public string UserSummary { get; set => SetProperty(ref field, value); } = "";
        public bool IsHeartOverlayVisible { get; set => SetProperty(ref field, value); }
        public object? IconSource { get; set => SetProperty(ref field, value); }
        public string IconStatusText { get; set => SetProperty(ref field, value); } = "Select a creator\nto view details";
        public bool IsViewContributionsVisible { get; set => SetProperty(ref field, value); }
        public bool IsViewObjectsVisible { get; set => SetProperty(ref field, value); }

        public ICommand RemoveCommand { get; }
        public ICommand ViewUserLevelsCommand { get; }
        public ICommand ViewUserContributionsCommand { get; }
        public ICommand ViewUserObjectsCommand { get; }
        public ICommand DownloadAllLevelsCommand { get; }

        public Action? RequestClose;

        public HeartedCreatorsWindowViewModel(IViewService viewService)
        {
            _viewService = viewService;

            RemoveCommand = new RelayCommand(ExecuteRemove, CanExecuteAction);
            ViewUserLevelsCommand = new RelayCommand(ExecuteViewUserLevels, CanExecuteAction);
            ViewUserContributionsCommand = new RelayCommand(ExecuteViewUserContributions, CanExecuteAction);
            ViewUserObjectsCommand = new RelayCommand(ExecuteViewUserObjects, CanExecuteAction);
            DownloadAllLevelsCommand = new RelayCommand(ExecuteDownloadAllLevels, CanExecuteAction);

            // Access owner window to determine available database features
            if (_viewService.GetMainWindow() is MainWindow mainWindow)
            {
                IsViewContributionsVisible = mainWindow.HasContributorsTable;
                IsViewObjectsVisible = mainWindow.HasObjectContributorsTable;
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
            HeartedList.AddRange(HeartedCreatorsManager.HeartedCreators);
            
            StatusText = $"You have {HeartedList.Count} hearted creator(s).";
            IsHeartOverlayVisible = false;

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
                IsHeartOverlayVisible = true;

                long expectedRequestId = Interlocked.Increment(ref _currentIconRequestId);
                if (_iconCts != null)
                {
                    _iconCts.Cancel();
                    _iconCts.Dispose();
                }
                _iconCts = new CancellationTokenSource();

                await LoadUserIconAsync(selected.IconHash, _iconCts.Token, expectedRequestId);
            }
            else
            {
                UserNpHandle = "";
                UserStats = "";
                UserSummary = "";
                IsHeartOverlayVisible = false;
                IconSource = null;
                IconStatusText = "Select a creator\nto view details";
            }
        }

        private async Task LoadUserIconAsync(string? hash, CancellationToken token, long expectedRequestId)
        {
            IconSource = null;

            if (string.IsNullOrEmpty(hash) || hash.Length <= 8)
            {
                IconStatusText = "No Icon Available";
                return;
            }

            IconStatusText = "Loading Icon...";

            var bmp = await IconLoaderService.LoadIconSourceAsync(hash, MainWindow.SharedHttpClient, token);

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

    }
}