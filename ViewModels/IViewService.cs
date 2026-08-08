using LbpArchiveToolkit.Models;
using System.Collections.Generic;

namespace LbpArchiveToolkit.ViewModels
{
    public interface IViewService
    {
        bool HasContributorsTable { get; }
        bool HasObjectContributorsTable { get; }

        void InitiateCreatorSearch(string npHandle);
        void InitiateContributionsSearch(string npHandle);
        void InitiateObjectsSearch(string npHandle);
        System.Threading.Tasks.Task InitiateBatchDownloadAsync(UserItem user);
        void ClearSavedLevels();
        bool ShowInputDialog(string message, string title, string defaultText, out string inputText);
        bool ShowMissingDatabaseDialog();
        void ShowSettingsDialog();
        (AdvancedSearchCriteria Criteria, bool ShouldSearch)? ShowAdvancedSearchDialog(AdvancedSearchCriteria current, bool hasCommunityLabels, bool hasExtendedSlotProperties);
        void ShowToast(string message, string targetElementName);
        void ShowContributorsDialog(List<string> contributors, List<string> objectContributors, List<(long id, string name)> objectOrigins, string levelCreator, System.Action<string> onCreatorClicked, System.Action<long> onLevelClicked);
        void ShowObjectUsagesDialog(List<(long id, string name)> levels, string originLevelName, System.Action<long> onLevelClicked);
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
        string? ShowOpenFileDialog(string filter, string title);
        string? ShowSaveFileDialog(string filter, string title, string defaultFileName);
        string? ShowOpenFolderDialog(string title);
        string? ShowImageCropDialog(string imagePath);
        (bool success, string newName, string newDesc, string? newIconPath, bool newLocked, bool newSubLevel, bool newShareable) ShowEditInfoDialog(string currentName, string currentDesc, string? currentIconPath, bool isLocked, bool isSubLevel, bool isShareable);
        void ShowTextureViewerDialog(string backupPath, string levelName);
        void SetClipboardText(string text);
        void SetClipboardImage(object image);
        void OpenUrl(string url);
        void OpenDirectory(string path);
        void SaveImageToFile(object image, string filePath);
        void InvokeOnUIThread(System.Action action);
        System.Threading.Tasks.Task InvokeOnUIThreadAsync(System.Action action);
    }
}