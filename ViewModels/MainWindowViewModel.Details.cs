using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace LbpArchiveToolkit.ViewModels
{
    public partial class MainWindowViewModel
    {
        private async void UpdateLevelDetails()
        {
            if (SelectedLevel == null)
            {
                IsMmPickVisible = false;
                IsLevelHeartOverlayVisible = false;
                IsIconLockVisible = false;
                IconScale = 1.0;
                LevelTags.Clear();
                IsToggleTagsButtonVisible = false;
                IsObjectOriginVisible = false;
                LevelIconSource = null;
                IconStatusText = "Select a level\nto view details";
                return;
            }

            var currentLevel = SelectedLevel;
            long currentRequestId = Interlocked.Increment(ref _currentIconRequestId);

            IsMmPickVisible = currentLevel.IsMmPick;
            IsLevelHeartOverlayVisible = HeartedLevelsManager.IsHearted(currentLevel.Id);
            IsIconLockVisible = currentLevel.IsLocked;
            IconScale = currentLevel.IsSubLevel ? 0.85 : 1.0;
            HeartLevelButtonText = HeartedLevelsManager.IsHearted(currentLevel.Id) ? "♡ UNHEART LEVEL" : "♥ HEART LEVEL";

            if (currentLevel.Hash == null || currentLevel.Description == null)
            {
                await _dbService.FetchLevelDetailsAsync(currentLevel);
            }

            if (SelectedLevel != currentLevel) return;

            OnPropertyChanged(nameof(SelectedLevelDescription));
            OnPropertyChanged(nameof(SelectedLevel));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();

            var labels = currentLevel.LabelsBlob != null ? LbpArchiveToolkit.Utils.LabelParser.ParseLabelNames(currentLevel.LabelsBlob) : new List<string>();
            var commLabels = currentLevel.CommunityLabelsBlob != null ? LbpArchiveToolkit.Utils.LabelParser.ParseLabelNames(currentLevel.CommunityLabelsBlob) : new List<string>();
            var tags = currentLevel.TagsBlob != null ? LbpArchiveToolkit.Utils.TagParser.ParseTagNames(currentLevel.TagsBlob) : new List<string>();

            bool hasAuthorLabels = labels.Count > 0;
            bool hasCommLabels = commLabels.Count > 0 && HasCommunityLabels;
            bool hasTags = tags.Count > 0;

            if (hasAuthorLabels || hasCommLabels || hasTags)
            {
                var addedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool showAsterisk = HasCommunityLabels;

                int tagIndex = 0;
                void AddOrUpdateTag(string text, string? toolTip, bool isVisible, bool isLbp1)
                {
                    if (tagIndex < LevelTags.Count)
                    {
                        var t = LevelTags[tagIndex];
                        t.Text = text;
                        t.ToolTip = toolTip;
                        t.TiltAngle = GetDeterministicTilt(text);
                        t.IsVisible = isVisible;
                        t.IsLbp1Tag = isLbp1;
                    }
                    else
                    {
                        LevelTags.Add(new TagItem { Text = text, ToolTip = toolTip, TiltAngle = GetDeterministicTilt(text), IsVisible = isVisible, IsLbp1Tag = isLbp1 });
                    }
                    tagIndex++;
                }

                if (hasAuthorLabels)
                {
                    foreach (var label in labels.OrderBy(l => l))
                    {
                        addedLabels.Add(label);
                        string displayName = showAsterisk ? label + "*" : label;
                        string? tooltip = showAsterisk ? "*Labels chosen by the author" : null;
                        AddOrUpdateTag(displayName, tooltip, true, false);
                    }
                }

                if (hasCommLabels)
                {
                    foreach (var label in commLabels.OrderBy(l => l))
                    {
                        if (addedLabels.Add(label))
                        {
                            AddOrUpdateTag(label, "Labels chosen by the community", true, false);
                        }
                    }
                }

                if (hasTags)
                {
                    foreach (var tag in tags.OrderBy(t => t))
                        AddOrUpdateTag(tag, null, false, true);

                    IsToggleTagsButtonVisible = true;
                    ToggleTagsButtonText = "SHOW TAGS";
                    _showingLbp1Tags = false;
                }
                else IsToggleTagsButtonVisible = false;

                while (LevelTags.Count > tagIndex)
                {
                    LevelTags.RemoveAt(LevelTags.Count - 1);
                }
            }
            else
            {
                LevelTags.Clear();
            }
            
            IsObjectOriginVisible = false;
            _ = LoadIconAsync(currentLevel.IconHash, currentRequestId);
            _ = CheckIfObjectOriginAsync(currentLevel.Id, currentRequestId);
            
            OnPropertyChanged(nameof(LevelCreatorText));
            OnPropertyChanged(nameof(LevelStatsText));
        }

        private async Task CheckIfObjectOriginAsync(long slotId, long expectedRequestId)
        {
            try
            {
                bool isOrigin = await _dbService.IsObjectOriginAsync(slotId);
                if (_currentIconRequestId == expectedRequestId)
                {
                    IsObjectOriginVisible = isOrigin;
                }
            }
            catch { }
        }

        private void UpdateUserDetails()
        {
            if (SelectedUser == null)
            {
                UserIconSource = null;
                UserIconStatusText = "Select a creator\nto view details";
                IsUserHeartOverlayVisible = false;
                OnPropertyChanged(nameof(UserStatsText));
                OnPropertyChanged(nameof(UserSummaryText));
                return;
            }

            long currentRequestId = Interlocked.Increment(ref _currentIconRequestId);

            IsUserHeartOverlayVisible = HeartedCreatorsManager.IsHearted(SelectedUser.NpHandle);
            UserHeartButtonText = HeartedCreatorsManager.IsHearted(SelectedUser.NpHandle) ? "♡ UNHEART CREATOR" : "♥ HEART CREATOR";
            OnPropertyChanged(nameof(UserStatsText));
            OnPropertyChanged(nameof(UserSummaryText));
            
            _ = LoadUserIconAsync(SelectedUser.IconHash, SelectedUser.NpHandle, currentRequestId);
        }

        private void ToggleTags()
        {
            _showingLbp1Tags = !_showingLbp1Tags;
            ToggleTagsButtonText = _showingLbp1Tags ? "HIDE TAGS" : "SHOW TAGS";
            foreach (var tag in LevelTags)
            {
                if (tag.IsLbp1Tag) tag.IsVisible = _showingLbp1Tags;
            }
        }

        private async void ToggleLevelHeart()
        {
            if (SelectedLevel != null)
            {
                if (SelectedLevel.Hash == null || SelectedLevel.Description == null)
                {
                    await _dbService.FetchLevelDetailsAsync(SelectedLevel);
                }

                if (HeartedLevelsManager.IsHearted(SelectedLevel.Id)) HeartedLevelsManager.Remove(SelectedLevel.Id);
                else HeartedLevelsManager.Add(SelectedLevel);
                
                UpdateLevelSavedString(SelectedLevel);
                RefreshHeartStates();
            }
        }

        private async void ToggleUserHeart(string? creator, bool fromContextMenu = false)
        {
            if (string.IsNullOrEmpty(creator)) return;
            
            string targetElement = fromContextMenu ? "ContextElement" : "Mouse";
            
            if (HeartedCreatorsManager.IsHearted(creator))
            {
                HeartedCreatorsManager.Remove(creator);
                _viewService.ShowToast("Unhearted", targetElement);
            }
            else
            {
                var user = UserResultsList.FirstOrDefault(u => u.NpHandle.Equals(creator, StringComparison.OrdinalIgnoreCase));
                
                if (user == null)
                {
                    try
                    {
                        var results = await _dbService.SearchUsersAsync(creator, true, "1", false, CancellationToken.None);
                        user = results.FirstOrDefault(u => u.NpHandle.Equals(creator, StringComparison.OrdinalIgnoreCase));
                    }
                    catch { }
                    
                    user ??= new UserItem { NpHandle = creator };
                }

                HeartedCreatorsManager.Add(user);
                _viewService.ShowToast("Hearted!", targetElement);
            }
            RefreshHeartStates();
        }

        private void UpdateLevelSavedString(LevelItem lvl)
        {
            bool isSaved = _savedLevels.Contains(lvl.Id);
            bool isHearted = HeartedLevelsManager.IsHearted(lvl.Id);
            bool isPlaylisted = PlaylistsManager.Playlists.Any(p => p.Levels.Any(l => l.Id == lvl.Id));

            string str = "";
            if (isSaved) str += "✓";
            if (isHearted) str += (str.Length > 0 ? " ♥" : "♥");
            if (isPlaylisted) str += (str.Length > 0 ? " ▶" : "▶");
            lvl.Saved = str;
        }

        private async Task LoadIconAsync(string? hash, long expectedRequestId)
        {
            LevelIconSource = null;
            if (string.IsNullOrEmpty(hash) || hash.Length <= 8) { IconStatusText = "No Icon Available"; return; }

            IconStatusText = "Loading Icon...";
            if (_iconCts != null)
            {
                _iconCts.Cancel();
                _iconCts.Dispose();
            }
            _iconCts = new CancellationTokenSource();

            var bmp = await IconLoaderService.LoadIconSourceAsync(hash, SharedHttpClient, _iconCts.Token);
            if (_currentIconRequestId != expectedRequestId || _iconCts.Token.IsCancellationRequested) return;

            if (bmp != null) { 
                LevelIconSource = bmp;
                IconStatusText = ""; 
            }
            else IconStatusText = "Icon offline\nor missing.";
        }

        private async Task LoadUserIconAsync(string? hash, string npHandle, long expectedRequestId)
        {
            UserIconSource = null;
            if (string.IsNullOrEmpty(hash) || hash.Length <= 8) { UserIconStatusText = "No Icon Available"; return; }

            UserIconStatusText = "Loading Icon...";
            if (_iconCts != null)
            {
                _iconCts.Cancel();
                _iconCts.Dispose();
            }
            _iconCts = new CancellationTokenSource();

            var bmp = await IconLoaderService.LoadIconSourceAsync(hash, SharedHttpClient, _iconCts.Token);
            if (_currentIconRequestId != expectedRequestId || _iconCts.Token.IsCancellationRequested) return;

            if (bmp != null) { UserIconSource = bmp; UserIconStatusText = ""; }
            else UserIconStatusText = "Icon offline\nor missing.";
        }

        private async Task ShowContributorsAsync()
        {
            if (SelectedLevel != null)
            {
                try
                {
                    var contributors = await _dbService.GetContributorsAsync(SelectedLevel.Id);
                    var objectContributors = await _dbService.GetObjectContributorsAsync(SelectedLevel.Id);
                    var objectOrigins = await _dbService.GetObjectOriginsAsync(SelectedLevel.Id);
                    _viewService.ShowContributorsDialog(contributors, objectContributors, objectOrigins, SelectedLevel.Creator ?? "Unknown", InitiateUserSearch, InitiateLevelSearch);
                }
                catch (Exception ex)
                {
                    _viewService.Alert($"Error fetching contributors: {ex.Message}", "Error");
                }
            }
        }

        private async Task ShowObjectUsagesAsync()
        {
            if (SelectedLevel != null)
            {
                try
                {
                    var levels = await _dbService.GetLevelsUsingObjectsFromAsync(SelectedLevel.Id);
                    _viewService.ShowObjectUsagesDialog(levels, SelectedLevel.LevelName ?? "Unknown", InitiateLevelSearch);
                }
                catch (Exception ex)
                {
                    _viewService.Alert($"Error fetching object usages: {ex.Message}", "Error");
                }
            }
        }

        private double GetDeterministicTilt(string str)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in str) hash = (hash ^ c) * 16777619;
                return ((hash % 10000) / 10000.0) * 6.0 - 3.0;
            }
        }
    }
}