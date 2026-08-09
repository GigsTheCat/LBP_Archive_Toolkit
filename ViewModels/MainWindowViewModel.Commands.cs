using LbpArchiveToolkit.Models;
using System.Linq;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public partial class MainWindowViewModel
    {
        #region Commands
        public ICommand SearchCommand { get; private set; } = null!;
        public ICommand CancelSearchCommand { get; private set; } = null!;
        public ICommand AutocompleteSelectedCommand { get; private set; } = null!;
        public ICommand SurpriseMeCommand { get; private set; } = null!;
        public ICommand BackCommand { get; private set; } = null!;
        public ICommand ForwardCommand { get; private set; } = null!;
        public ICommand OpenAdvancedSearchCommand { get; private set; } = null!;
        
        public ICommand ExtractSelectedCommand { get; private set; } = null!;
        public ICommand BatchDownloadCommand { get; private set; } = null!;
        public ICommand ToggleTagsCommand { get; private set; } = null!;
        public ICommand HeartLevelCommand { get; private set; } = null!;
        public ICommand CopyHashCommand { get; private set; } = null!;
        public ICommand CopyLevelNameCommand { get; private set; } = null!;
        public ICommand ShowContributorsCommand { get; private set; } = null!;
        public ICommand ShowObjectUsagesCommand { get; private set; } = null!;
        
        public ICommand SearchCreatorCommand { get; private set; } = null!;
        public ICommand HeartUserCommand { get; private set; } = null!;
        public ICommand ViewUserLevelsCommand { get; private set; } = null!;
        public ICommand ViewUserContributionsCommand { get; private set; } = null!;
        public ICommand ViewUserObjectsCommand { get; private set; } = null!;
        
        public ICommand OpenSettingsCommand { get; private set; } = null!;
        public ICommand OpenBackupManagerCommand { get; private set; } = null!;
        public ICommand OpenHeartedLevelsCommand { get; private set; } = null!;
        public ICommand OpenHeartedCreatorsCommand { get; private set; } = null!;
        public ICommand OpenPlaylistsCommand { get; private set; } = null!;
        public ICommand AddToPlaylistCommand { get; private set; } = null!;
        public ICommand OpenDownloadsCommand { get; private set; } = null!;
        public ICommand OpenLogViewerCommand { get; private set; } = null!;
        public ICommand OpenAboutCommand { get; private set; } = null!;
        #endregion

        public void InvalidateCommands()
        {
            (BackCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ForwardCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExtractSelectedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BatchDownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (HeartLevelCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CopyHashCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowContributorsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowObjectUsagesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SearchCreatorCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (HeartUserCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ViewUserLevelsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ViewUserContributionsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ViewUserObjectsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (AddToPlaylistCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void InitializeCommands()
        {
            AutocompleteSelectedCommand = new RelayCommand(param => 
            {
                if (param is AutocompleteSuggestion suggestion)
                {
                    // Suppress automatic UI search triggers to prevent double-searching
                    // and corrupting the back-arrow history stack
                    _isApplyingState = true;
                    try
                    {
                        SearchTypeIndex = suggestion.SearchTypeIndex; 
                        SearchText = suggestion.QueryText;

                        // Clear UI filters for exact entity searches to guarantee the match isn't hidden by them
                        GameIndex = 0;
                        SelectedGenre = "All Genres";
                        SearchDesc = false;
                        _advancedCriteria = new AdvancedSearchCriteria();
                        
                        // Enforce Exact Match for creator searches so we don't pull up similar names
                        ExactMatch = suggestion.SearchTypeIndex == 1;
                    }
                    finally
                    {
                        _isApplyingState = false;
                    }
                    
                    IsAutocompleteOpen = false;
                    SearchCommand.Execute(null);
                }
            });

            SearchCommand = new RelayCommand(_ => 
            {
                IsAutocompleteOpen = false;
                _ = SearchAsync();
            });
            CancelSearchCommand = new RelayCommand(_ => { _searchCts?.Cancel(); StatusText = "Cancelling search..."; });
            SurpriseMeCommand = new RelayCommand(_ => _ = SurpriseMeAsync());
            BackCommand = new RelayCommand(_ => NavigateBack(), _ => _searchHistory.Count > 0);
            ForwardCommand = new RelayCommand(_ => NavigateForward(), _ => _forwardHistory.Count > 0);
            
            OpenAdvancedSearchCommand = new RelayCommand(_ =>
            {
                var result = _viewService.ShowAdvancedSearchDialog(_advancedCriteria, HasCommunityLabels, HasExtendedSlotProperties);
                if (result != null)
                {
                    _advancedCriteria = result.Value.Criteria;
                    if (result.Value.ShouldSearch && !string.IsNullOrWhiteSpace(SearchText)) SearchCommand.Execute(null);
                }
            });

            ExtractSelectedCommand = new RelayCommand(param => 
            {
                if (param is System.Collections.IList items)
                {
                    var list = items.Cast<LevelItem>().ToList();
                    if (list.Any()) _ = ExtractLevelsAsync(list);
                }
            }, _ => SelectedLevel != null);

            BatchDownloadCommand = new RelayCommand(_ => _ = BatchDownloadAsync(SelectedUser!), _ => SelectedUser != null);
            ToggleTagsCommand = new RelayCommand(_ => ToggleTags());
            HeartLevelCommand = new RelayCommand(_ => ToggleLevelHeart(), _ => SelectedLevel != null);
            
            CopyHashCommand = new RelayCommand(_ => 
            {
                if (!string.IsNullOrEmpty(SelectedLevel?.Hash))
                {
                    _viewService.SetClipboardText(SelectedLevel.Hash);
                    _viewService.ShowToast("Hash Copied!", "btnCopyHash");
                }
            }, _ => SelectedLevel != null && !string.IsNullOrEmpty(SelectedLevel.Hash));

            CopyLevelNameCommand = new RelayCommand(param => 
            {
                if (param is LevelItem level && !string.IsNullOrEmpty(level.LevelName))
                {
                    _viewService.SetClipboardText(level.LevelName);
                    _viewService.ShowToast("Level Name Copied!", "ContextElement");
                }
            });

            ShowContributorsCommand = new RelayCommand(_ => _ = ShowContributorsAsync(), _ => SelectedLevel != null);
            ShowObjectUsagesCommand = new RelayCommand(_ => _ = ShowObjectUsagesAsync(), _ => SelectedLevel != null);
            
            SearchCreatorCommand = new RelayCommand(param => 
            {
                if (param is LevelItem level)
                {
                    InitiateUserSearch(level.Creator ?? "");
                }
            }, param => param is LevelItem l && !string.IsNullOrEmpty(l.Creator));

            HeartUserCommand = new RelayCommand(param => 
            {
                if (param is LevelItem level) ToggleUserHeart(level.Creator, true);
                else ToggleUserHeart(SelectedUser?.NpHandle, false);
            }, param => param is LevelItem l ? !string.IsNullOrEmpty(l.Creator) : SelectedUser != null);

            ViewUserLevelsCommand = new RelayCommand(
                param => InitiateCreatorSearch(param is LevelItem l ? l.Creator! : SelectedUser!.NpHandle),
                param => param is LevelItem l ? !string.IsNullOrEmpty(l.Creator) : SelectedUser != null);

            ViewUserContributionsCommand = new RelayCommand(
                param => InitiateContributionsSearch(param is LevelItem l ? l.Creator! : SelectedUser!.NpHandle),
                param => param is LevelItem l ? !string.IsNullOrEmpty(l.Creator) : SelectedUser != null);

            ViewUserObjectsCommand = new RelayCommand(
                param => InitiateObjectsSearch(param is LevelItem l ? l.Creator! : SelectedUser!.NpHandle),
                param => param is LevelItem l ? !string.IsNullOrEmpty(l.Creator) : SelectedUser != null);

            OpenSettingsCommand = new RelayCommand(_ => 
            {
                _viewService.ShowSettingsDialog();
                RefreshDatabaseService();
            });
            
            OpenBackupManagerCommand = new RelayCommand(_ => _viewService.OpenBackupManager());
            OpenHeartedLevelsCommand = new RelayCommand(_ => 
            {
                _viewService.OpenHeartedLevels();
                RefreshHeartStates();
            });
             OpenHeartedCreatorsCommand = new RelayCommand(_ => 
            {
                _viewService.OpenHeartedCreators();
                RefreshHeartStates();
            });
            OpenPlaylistsCommand = new RelayCommand(_ => 
            {
                _viewService.OpenPlaylists();
                RefreshHeartStates();
            });
            AddToPlaylistCommand = new RelayCommand(async _ => 
            {
                if (SelectedLevel != null)
                {
                    if (SelectedLevel.Hash == null || SelectedLevel.Description == null)
                    {
                        await _dbService.FetchLevelDetailsAsync(SelectedLevel);
                    }
                    _viewService.ShowAddToPlaylistDialog(SelectedLevel);
                    RefreshHeartStates();
                }
            }, _ => SelectedLevel != null);
            OpenDownloadsCommand = new RelayCommand(_ => _viewService.OpenDownloads());
            OpenLogViewerCommand = new RelayCommand(_ => _viewService.OpenLogViewer());
            OpenAboutCommand = new RelayCommand(_ => _viewService.OpenAbout());
        }
    }
}