using LbpArchiveToolkit.Models;
using System.Collections.Generic;
using System.Windows;

namespace LbpArchiveToolkit.ViewModels
{
    public interface IViewService
    {
        Window GetMainWindow();
        bool ShowMissingDatabaseDialog();
        void ShowSettingsDialog();
        (AdvancedSearchCriteria Criteria, bool ShouldSearch)? ShowAdvancedSearchDialog(AdvancedSearchCriteria current, bool hasCommunityLabels, bool hasExtendedSlotProperties);
        void ShowToast(string message, string targetElementName);
        void ShowContributorsDialog(List<string> contributors, List<string> objectContributors, string levelCreator, System.Action<string> onCreatorClicked);
        bool Confirm(string message, string title);
        void Alert(string message, string title);
        
        void OpenBackupManager();
        void OpenHeartedLevels();
        void OpenHeartedCreators();
        void OpenPlaylists();
        void ShowAddToPlaylistDialog(LevelItem level);
        void OpenDownloads();
        void OpenLogViewer();
        void OpenAbout();
    }
}