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
            if (_selectedLevel == null)
            {
                IconEllipseStroke = new SolidColorBrush(Color.FromRgb(255, 183, 3));
                MmPickVisibility = Visibility.Hidden;
                LevelHeartOverlayVisibility = Visibility.Hidden;
                LevelTags.Clear();
                ToggleTagsButtonVisibility = Visibility.Collapsed;
                IconEllipseFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
                IconStatusText = "Select a level\nto view details";
                return;
            }

            MmPickVisibility = _selectedLevel.IsMmPick ? Visibility.Visible : Visibility.Hidden;
            IconEllipseStroke = _selectedLevel.IsMmPick ? new SolidColorBrush(Color.FromRgb(247, 37, 133)) : new SolidColorBrush(Color.FromRgb(255, 183, 3));
            LevelHeartOverlayVisibility = HeartedLevelsManager.IsHearted(_selectedLevel.Id) ? Visibility.Visible : Visibility.Hidden;
            HeartLevelButtonText = HeartedLevelsManager.IsHearted(_selectedLevel.Id) ? "♡ UNHEART LEVEL" : "♥ HEART LEVEL";

            LevelTags.Clear();
            bool hasAuthorLabels = _selectedLevel.Labels != null && _selectedLevel.Labels.Count > 0;
            bool hasCommLabels = _selectedLevel.CommunityLabels != null && _selectedLevel.CommunityLabels.Count > 0 && HasCommunityLabels;
            bool hasTags = _selectedLevel.Tags != null && _selectedLevel.Tags.Count > 0;

            if (hasAuthorLabels || hasCommLabels || hasTags)
            {
                var addedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool showAsterisk = HasCommunityLabels;

                if (hasAuthorLabels)
                {
                    foreach (var label in _selectedLevel.Labels!.OrderBy(l => l))
                    {
                        addedLabels.Add(label);
                        string displayName = showAsterisk ? label + "*" : label;
                        string? tooltip = showAsterisk ? "*Labels chosen by the author" : null;
                        LevelTags.Add(new TagItem { Text = displayName, ToolTip = tooltip, TiltAngle = GetDeterministicTilt(label), Visibility = Visibility.Visible, IsLbp1Tag = false });
                    }
                }

                if (hasCommLabels)
                {
                    foreach (var label in _selectedLevel.CommunityLabels!.OrderBy(l => l))
                    {
                        if (addedLabels.Add(label))
                        {
                            LevelTags.Add(new TagItem { Text = label, ToolTip = "Labels chosen by the community", TiltAngle = GetDeterministicTilt(label), Visibility = Visibility.Visible, IsLbp1Tag = false });
                        }
                    }
                }

                if (hasTags)
                {
                    foreach (var tag in _selectedLevel.Tags!.OrderBy(t => t))
                        LevelTags.Add(new TagItem { Text = tag, TiltAngle = GetDeterministicTilt(tag), Visibility = Visibility.Collapsed, IsLbp1Tag = true });
                    ToggleTagsButtonVisibility = Visibility.Visible;
                    ToggleTagsButtonText = "SHOW TAGS";
                    _showingLbp1Tags = false;
                }
                else ToggleTagsButtonVisibility = Visibility.Collapsed;
            }
            
            _ = LoadIconAsync(_selectedLevel.IconHash);
            OnPropertyChanged(nameof(LevelCreatorAndStatsText));
        }

        private void UpdateUserDetails()
        {
            if (_selectedUser == null)
            {
                UserIconRectFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
                UserIconStatusText = "Select a creator\nto view details";
                UserHeartOverlayVisibility = Visibility.Hidden;
                OnPropertyChanged(nameof(UserStatsText));
                OnPropertyChanged(nameof(UserSummaryText));
                return;
            }

            UserHeartOverlayVisibility = HeartedCreatorsManager.IsHearted(_selectedUser.NpHandle) ? Visibility.Visible : Visibility.Hidden;
            UserHeartButtonText = HeartedCreatorsManager.IsHearted(_selectedUser.NpHandle) ? "♡ UNHEART CREATOR" : "♥ HEART CREATOR";
            OnPropertyChanged(nameof(UserStatsText));
            OnPropertyChanged(nameof(UserSummaryText));
            
            _ = LoadUserIconAsync(_selectedUser.IconHash, _selectedUser.NpHandle);
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

        private async void ToggleUserHeart(string? creator)
        {
            if (string.IsNullOrEmpty(creator)) return;
            if (HeartedCreatorsManager.IsHearted(creator))
            {
                HeartedCreatorsManager.Remove(creator);
                _viewService.ShowToast("Unhearted", "dgResults");
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
                _viewService.ShowToast("Hearted!", "dgResults");
            }
            RefreshHeartStates();
        }

        private void UpdateLevelSavedString(LevelItem lvl)
        {
            bool isSaved = _savedLevels.Contains(lvl.Id);
            bool isHearted = HeartedLevelsManager.IsHearted(lvl.Id);
            string str = "";
            if (isSaved) str += "✓";
            if (isHearted) str += (str.Length > 0 ? " ♥" : "♥");
            lvl.Saved = str;
        }

        private async Task LoadIconAsync(string? hash)
        {
            IconEllipseFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
            if (string.IsNullOrEmpty(hash) || hash.Length <= 8) { IconStatusText = "No Icon Available"; return; }

            IconStatusText = "Loading Icon...";
            long expectedRequestId = Interlocked.Increment(ref _currentIconRequestId);
            _iconCts?.Cancel();
            _iconCts = new CancellationTokenSource();

            var brush = await IconLoaderService.LoadIconBrushAsync(hash, SharedHttpClient, _iconCts.Token);
            if (_currentIconRequestId != expectedRequestId || _iconCts.Token.IsCancellationRequested) return;

            if (brush != null) { IconEllipseFill = brush; IconStatusText = ""; }
            else IconStatusText = "Icon offline\nor missing.";
        }

        private async Task LoadUserIconAsync(string? hash, string npHandle)
        {
            UserIconRectFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
            if (string.IsNullOrEmpty(hash) || hash.Length <= 8) { UserIconStatusText = "No Icon Available"; return; }

            UserIconStatusText = "Loading Icon...";
            long expectedRequestId = Interlocked.Increment(ref _currentIconRequestId);
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
                    _viewService.ShowContributorsDialog(contributors, objectContributors, SelectedLevel.Creator ?? "Unknown", InitiateUserSearch);
                }
                catch (Exception ex)
                {
                    _viewService.Alert($"Error fetching contributors: {ex.Message}", "Error");
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