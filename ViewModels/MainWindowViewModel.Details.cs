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
        private void UpdateLevelDetails()
        {
            if (SelectedLevel == null)
            {
                IconEllipseStroke = new SolidColorBrush(Color.FromRgb(255, 183, 3));
                MmPickVisibility = Visibility.Hidden;
                LevelHeartOverlayVisibility = Visibility.Hidden;
                IconLockVisibility = Visibility.Hidden;
                IconScale = 1.0;
                LevelTags.Clear();
                ToggleTagsButtonVisibility = Visibility.Collapsed;
                ObjectOriginVisibility = Visibility.Collapsed;
                IconEllipseFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
                OriginalIconFill = IconEllipseFill;
                IconStatusText = "Select a level\nto view details";
                return;
            }

            MmPickVisibility = SelectedLevel.IsMmPick ? Visibility.Visible : Visibility.Hidden;
            IconEllipseStroke = SelectedLevel.IsMmPick ? new SolidColorBrush(Color.FromRgb(247, 37, 133)) : new SolidColorBrush(Color.FromRgb(255, 183, 3));
            LevelHeartOverlayVisibility = HeartedLevelsManager.IsHearted(SelectedLevel.Id) ? Visibility.Visible : Visibility.Hidden;
            IconLockVisibility = SelectedLevel.IsLocked ? Visibility.Visible : Visibility.Hidden;
            IconScale = SelectedLevel.IsSubLevel ? 0.85 : 1.0;
            HeartLevelButtonText = HeartedLevelsManager.IsHearted(SelectedLevel.Id) ? "♡ UNHEART LEVEL" : "♥ HEART LEVEL";

            LevelTags.Clear();
            bool hasAuthorLabels = SelectedLevel.Labels != null && SelectedLevel.Labels.Count > 0;
            bool hasCommLabels = SelectedLevel.CommunityLabels != null && SelectedLevel.CommunityLabels.Count > 0 && HasCommunityLabels;
            bool hasTags = SelectedLevel.Tags != null && SelectedLevel.Tags.Count > 0;

            if (hasAuthorLabels || hasCommLabels || hasTags)
            {
                var addedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool showAsterisk = HasCommunityLabels;
                var newTags = new List<TagItem>();

                if (hasAuthorLabels)
                {
                    foreach (var label in SelectedLevel.Labels!.OrderBy(l => l))
                    {
                        addedLabels.Add(label);
                        string displayName = showAsterisk ? label + "*" : label;
                        string? tooltip = showAsterisk ? "*Labels chosen by the author" : null;
                        newTags.Add(new TagItem { Text = displayName, ToolTip = tooltip, TiltAngle = GetDeterministicTilt(label), Visibility = Visibility.Visible, IsLbp1Tag = false });
                    }
                }

                if (hasCommLabels)
                {
                    foreach (var label in SelectedLevel.CommunityLabels!.OrderBy(l => l))
                    {
                        if (addedLabels.Add(label))
                        {
                            newTags.Add(new TagItem { Text = label, ToolTip = "Labels chosen by the community", TiltAngle = GetDeterministicTilt(label), Visibility = Visibility.Visible, IsLbp1Tag = false });
                        }
                    }
                }

                if (hasTags)
                {
                    foreach (var tag in SelectedLevel.Tags!.OrderBy(t => t))
                        newTags.Add(new TagItem { Text = tag, TiltAngle = GetDeterministicTilt(tag), Visibility = Visibility.Collapsed, IsLbp1Tag = true });
                    ToggleTagsButtonVisibility = Visibility.Visible;
                    ToggleTagsButtonText = "SHOW TAGS";
                    _showingLbp1Tags = false;
                }
                else ToggleTagsButtonVisibility = Visibility.Collapsed;

                if (newTags.Count > 0)
                {
                    LevelTags.AddRange(newTags);
                }
            }
            
            long currentRequestId = Interlocked.Increment(ref _currentIconRequestId);

            ObjectOriginVisibility = Visibility.Collapsed;
            _ = LoadIconAsync(SelectedLevel.IconHash, currentRequestId);
            _ = CheckIfObjectOriginAsync(SelectedLevel.Id, currentRequestId);
            
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
                    ObjectOriginVisibility = isOrigin ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void UpdateUserDetails()
        {
            if (SelectedUser == null)
            {
                UserIconRectFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
                UserIconStatusText = "Select a creator\nto view details";
                UserHeartOverlayVisibility = Visibility.Hidden;
                OnPropertyChanged(nameof(UserStatsText));
                OnPropertyChanged(nameof(UserSummaryText));
                return;
            }

            long currentRequestId = Interlocked.Increment(ref _currentIconRequestId);

            UserHeartOverlayVisibility = HeartedCreatorsManager.IsHearted(SelectedUser.NpHandle) ? Visibility.Visible : Visibility.Hidden;
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
                if (tag.IsLbp1Tag) tag.Visibility = _showingLbp1Tags ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ToggleLevelHeart()
        {
            if (SelectedLevel != null)
            {
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
            IconEllipseFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
            OriginalIconFill = IconEllipseFill;
            if (string.IsNullOrEmpty(hash) || hash.Length <= 8) { IconStatusText = "No Icon Available"; return; }

            IconStatusText = "Loading Icon...";
            _iconCts?.Cancel();
            _iconCts = new CancellationTokenSource();

            var brush = await IconLoaderService.LoadIconBrushAsync(hash, SharedHttpClient, _iconCts.Token);
            if (_currentIconRequestId != expectedRequestId || _iconCts.Token.IsCancellationRequested) return;

            if (brush != null) { 
                OriginalIconFill = brush;
                if (SelectedLevel != null && SelectedLevel.IsLocked && brush.ImageSource is System.Windows.Media.Imaging.BitmapSource bmp)
                {
                    var grayscaleBmp = new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0);
                    grayscaleBmp.Freeze();
                    var grayBrush = new ImageBrush(grayscaleBmp) { Stretch = Stretch.UniformToFill };
                    grayBrush.Freeze();
                    IconEllipseFill = grayBrush;
                }
                else IconEllipseFill = brush;
                IconStatusText = ""; 
            }
            else IconStatusText = "Icon offline\nor missing.";
        }

        private async Task LoadUserIconAsync(string? hash, string npHandle, long expectedRequestId)
        {
            UserIconRectFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
            if (string.IsNullOrEmpty(hash) || hash.Length <= 8) { UserIconStatusText = "No Icon Available"; return; }

            UserIconStatusText = "Loading Icon...";
            _iconCts?.Cancel();
            _iconCts = new CancellationTokenSource();

            var brush = await IconLoaderService.LoadIconBrushAsync(hash, SharedHttpClient, _iconCts.Token);
            if (_currentIconRequestId != expectedRequestId || _iconCts.Token.IsCancellationRequested) return;

            if (brush != null) { UserIconRectFill = brush; UserIconStatusText = ""; }
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