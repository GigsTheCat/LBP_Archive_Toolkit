using LbpArchiveToolkit.Models;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public partial class MainWindowViewModel
    {
        #region Commands
        public ICommand SearchCommand { get; private set; } = null!;
        public ICommand CancelSearchCommand { get; private set; } = null!;
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
        
        public ICommand SearchCreatorCommand { get; private set; } = null!;
        public ICommand HeartUserCommand { get; private set; } = null!;
        public ICommand ViewUserLevelsCommand { get; private set; } = null!;
        public ICommand ViewUserContributionsCommand { get; private set; } = null!;
        public ICommand ViewUserObjectsCommand { get; private set; } = null!;
        
        public ICommand OpenSettingsCommand { get; private set; } = null!;
        public ICommand OpenBackupManagerCommand { get; private set; } = null!;
        public ICommand OpenHeartedLevelsCommand { get; private set; } = null!;
        public ICommand OpenHeartedCreatorsCommand { get; private set; } = null!;
        public ICommand OpenDownloadsCommand { get; private set; } = null!;
        public ICommand OpenLogViewerCommand { get; private set; } = null!;
        public ICommand OpenAboutCommand { get; private set; } = null!;
        #endregion

        private void InitializeCommands()
        {
            SearchCommand = new RelayCommand(_ => _ = SearchAsync());
            CancelSearchCommand = new RelayCommand(_ => { _searchCts?.Cancel(); StatusText = "Cancelling search..."; });
            SurpriseMeCommand = new RelayCommand(_ => _ = SurpriseMeAsync());
            BackCommand = new RelayCommand(_ => NavigateBack(), _ => _searchHistory.Count > 0);
            ForwardCommand = new RelayCommand(_ => NavigateForward(), _ => _forwardHistory.Count > 0);
            
            OpenAdvancedSearchCommand = new RelayCommand(_ =>
            {
                var result = _viewService.ShowAdvancedSearchDialog(_advancedCriteria);
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
                    Clipboard.SetText(SelectedLevel.Hash);
                    _viewService.ShowToast("Hash Copied!", "btnCopyHash");
                }
            }, _ => SelectedLevel != null && !string.IsNullOrEmpty(SelectedLevel.Hash));

            CopyLevelNameCommand = new RelayCommand(param => 
            {
                if (param is LevelItem level && !string.IsNullOrEmpty(level.LevelName))
                {
                    Clipboard.SetText(level.LevelName);
                    _viewService.ShowToast("Level Name Copied!", "dgResults");
                }
            });

            ShowContributorsCommand = new RelayCommand(_ => _ = ShowContributorsAsync(), _ => SelectedLevel != null);
            
            SearchCreatorCommand = new RelayCommand(param => 
            {
                if (param is LevelItem level)
                {
                    SearchText = level.Creator ?? "";
                    if (SearchTypeIndex == 1) SearchCommand.Execute(null);
                    else SearchTypeIndex = 1;
                }
            });

            HeartUserCommand = new RelayCommand(param => 
            {
                if (param is LevelItem level) ToggleUserHeart(level.Creator);
                else ToggleUserHeart(SelectedUser?.NpHandle);
            });

            ViewUserLevelsCommand = new RelayCommand(param => InitiateCreatorSearch(param is LevelItem l ? l.Creator! : SelectedUser!.NpHandle));
            ViewUserContributionsCommand = new RelayCommand(param => InitiateContributionsSearch(param is LevelItem l ? l.Creator! : SelectedUser!.NpHandle));
            ViewUserObjectsCommand = new RelayCommand(param => InitiateObjectsSearch(param is LevelItem l ? l.Creator! : SelectedUser!.NpHandle));

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
            OpenDownloadsCommand = new RelayCommand(_ => _viewService.OpenDownloads());
            OpenLogViewerCommand = new RelayCommand(_ => _viewService.OpenLogViewer());
            OpenAboutCommand = new RelayCommand(_ => _viewService.OpenAbout());
        }
    }
}