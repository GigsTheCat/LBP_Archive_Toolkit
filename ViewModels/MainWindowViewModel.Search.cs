using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public partial class MainWindowViewModel
    {
        private async Task SearchAsync()
        {
            string keyword = SearchText?.Trim() ?? "";
            bool hasAdvancedFilters = _advancedCriteria.MinHearts > 0 || _advancedCriteria.MinPlays > 0 || _advancedCriteria.MinHeartPercentage > 0 || _advancedCriteria.MinYayPercentage > 0 || _advancedCriteria.MinClearPercentage > 0 || _advancedCriteria.MaxClearPercentage < 100 || _advancedCriteria.IsTeamPick || _advancedCriteria.RequireLocked || _advancedCriteria.RequireSubLevel || _advancedCriteria.RequireShareable || _advancedCriteria.ExcludeTeamPick || _advancedCriteria.ExcludeLocked || _advancedCriteria.ExcludeSubLevels || _advancedCriteria.ExcludeShareable || _advancedCriteria.RequiredLabels.Count > 0 || _advancedCriteria.RequiredTags.Count > 0;

            if (string.IsNullOrWhiteSpace(keyword) && LimitIndex == 4 && !hasAdvancedFilters)
            {
                _viewService.Alert("Performing a blank search with 'All' results and no advanced filters will load too many results and may crash the application.\n\nPlease add a search keyword, reduce the limit, or apply advanced filters.", "Search Too Broad");
                return;
            }

            IsSearching = true;
            StatusText = "Searching database...";
            IsProgressVisible = Visibility.Visible;
            IsProgressIndeterminate = true;

            var current = _currentSearch;
            if (current != null)
            {
                if (current.SearchTypeIndex == 0 || current.SearchTypeIndex == 2 || current.SearchTypeIndex == 3)
                    current.SelectedItem = SelectedLevel;
                else
                    current.SelectedUser = SelectedUser;

                PushToHistory(_searchHistory, current);
            }

            ResultsList.Clear();
            UserResultsList = new List<UserItem>();
            _forwardHistory.Clear();
            CommandManager.InvalidateRequerySuggested();

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var searchToken = _searchCts.Token;

            string? limitFilter = LimitIndex == 4 ? "All" : (LimitIndex == 3 ? "1000" : (LimitIndex == 2 ? "500" : (LimitIndex == 1 ? "200" : "100")));
            string? genreFilter = SelectedGenre;

            try
            {
                if (IsLevelSearch)
                {
                    bool searchContribs = SearchTypeIndex == 2;
                    bool searchObjects = SearchTypeIndex == 3;
                    await PerformLevelSearchAsync(keyword, ExactMatch, SearchDesc, GameIndex, genreFilter, limitFilter, _advancedCriteria, searchToken, "Found", searchContribs, searchObjects);

                    var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ResultsList);
                    SelectedLevel = view.Cast<LevelItem>().FirstOrDefault();

                    _currentSearch = new SearchState
                    {
                        SearchText = keyword, SearchTypeIndex = SearchTypeIndex, GameIndex = GameIndex,
                        Genre = genreFilter ?? "All Genres", LimitIndex = LimitIndex, Exact = ExactMatch,
                        SearchDesc = SearchDesc,
                        AdvancedCriteria = new AdvancedSearchCriteria
                        {
                            MinHearts = _advancedCriteria.MinHearts, MinPlays = _advancedCriteria.MinPlays,
                            MinHeartPercentage = _advancedCriteria.MinHeartPercentage,
                            MinYayPercentage = _advancedCriteria.MinYayPercentage,
                            MinClearPercentage = _advancedCriteria.MinClearPercentage,
                            MaxClearPercentage = _advancedCriteria.MaxClearPercentage,
                            IsTeamPick = _advancedCriteria.IsTeamPick,
                            RequireLocked = _advancedCriteria.RequireLocked,
                            RequireSubLevel = _advancedCriteria.RequireSubLevel,
                            RequireShareable = _advancedCriteria.RequireShareable,
                            RequiredLabels = new List<string>(_advancedCriteria.RequiredLabels),
                            RequiredTags = new List<string>(_advancedCriteria.RequiredTags),
                            LabelMatchMode = _advancedCriteria.LabelMatchMode,
                            ExcludedLabels = new List<string>(_advancedCriteria.ExcludedLabels),
                            ExcludedTags = new List<string>(_advancedCriteria.ExcludedTags),
                            ExcludedCreators = _advancedCriteria.ExcludedCreators,
                            ExcludedContributors = _advancedCriteria.ExcludedContributors,
                            ExcludedObjectContributors = _advancedCriteria.ExcludedObjectContributors,
                            PublishedBefore = _advancedCriteria.PublishedBefore,
                            PublishedAfter = _advancedCriteria.PublishedAfter,
                            ExcludeTeamPick = _advancedCriteria.ExcludeTeamPick,
                            ExcludeLocked = _advancedCriteria.ExcludeLocked,
                            ExcludeSubLevels = _advancedCriteria.ExcludeSubLevels,
                            ExcludeShareable = _advancedCriteria.ExcludeShareable,
                            MaxHearts = _advancedCriteria.MaxHearts,
                            MaxPlays = _advancedCriteria.MaxPlays
                            }
                        };
                }
                 else
                {
                    await PerformUserSearchAsync(keyword, ExactMatch, limitFilter, searchToken, "Found", false);
                    if (UserResultsList.Count > 0) SelectedUser = UserResultsList[0];

                    _currentSearch = new SearchState
                    {
                        SearchText = keyword, SearchTypeIndex = 1, LimitIndex = LimitIndex, Exact = ExactMatch,
                        AdvancedCriteria = new AdvancedSearchCriteria()
                    };
                }
            }
            catch (FileNotFoundException)
            {
                if (_viewService.ShowMissingDatabaseDialog())
                {
                    _viewService.ShowSettingsDialog();
                    RefreshDatabaseService();
                    if (File.Exists(ConfigManager.DatabasePath))
                    {
                        _ = SearchAsync();
                        return;
                    }
                }
                StatusText = "Search failed. Database missing.";
            }
            catch (OperationCanceledException) { StatusText = "Search cancelled."; }
            catch (Exception ex)
            {
                _viewService.Alert($"Database Error: {ex.Message}", "Error");
                StatusText = "Search failed.";
            }
            finally
            {
                IsSearching = false;
                IsProgressVisible = Visibility.Hidden;
            }
        }

        private async Task SurpriseMeAsync()
        {
            IsSearching = true;
            StatusText = SearchTypeIndex == 1 ? "Finding a random creator..." : "Finding a random level...";
            IsProgressVisible = Visibility.Visible;
            IsProgressIndeterminate = true;

            var current = _currentSearch;
            if (current != null)
            {
                if (current.SearchTypeIndex == 0 || current.SearchTypeIndex == 2 || current.SearchTypeIndex == 3)
                    current.SelectedItem = SelectedLevel;
                else
                    current.SelectedUser = SelectedUser;

                PushToHistory(_searchHistory, current);
            }

            ResultsList.Clear();
            UserResultsList = new List<UserItem>();
            _forwardHistory.Clear();
            CommandManager.InvalidateRequerySuggested();

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var searchToken = _searchCts.Token;

            string keyword = SearchText?.Trim() ?? "";
            string? limitFilter = LimitIndex == 4 ? "All" : (LimitIndex == 3 ? "1000" : (LimitIndex == 2 ? "500" : (LimitIndex == 1 ? "200" : "100")));
            string? genreFilter = SelectedGenre;

            try
            {
                if (IsLevelSearch)
                {
                    var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ResultsList);
                    view.SortDescriptions.Clear();

                    var progressReporter = new Progress<string>(status => StatusText = status);

                    await Task.Run(async () =>
                    {
                        await foreach (var lvl in _dbService.SearchLevelsAsync(keyword, ExactMatch, SearchDesc, GameIndex, genreFilter, limitFilter, _savedLevels.ToHashSet(), HeartedLevelsManager.HeartedLevels.Select(x => x.Id).ToHashSet(), _advancedCriteria, progressReporter, SearchTypeIndex == 2, SearchTypeIndex == 3, true, searchToken).ConfigureAwait(false))
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                ResultsList.Add(lvl);
                                SelectedLevel = lvl;
                            });
                        }
                    });

                    if (ResultsList.Count == 0)
                        StatusText = "No levels matched the random search criteria.";
                    else
                        StatusText = "Surprise! Found a random level.";

                    _currentSearch = new SearchState
                    {
                        SearchText = keyword, SearchTypeIndex = SearchTypeIndex, GameIndex = GameIndex,
                        Genre = genreFilter ?? "All Genres", LimitIndex = LimitIndex, Exact = ExactMatch,
                        SearchDesc = SearchDesc,
                        SelectedItem = SelectedLevel,
                        IsSurpriseMe = true,
                        AdvancedCriteria = new AdvancedSearchCriteria
                        {
                            MinHearts = _advancedCriteria.MinHearts, MinPlays = _advancedCriteria.MinPlays,
                            MinHeartPercentage = _advancedCriteria.MinHeartPercentage,
                            MinYayPercentage = _advancedCriteria.MinYayPercentage,
                            MinClearPercentage = _advancedCriteria.MinClearPercentage,
                            MaxClearPercentage = _advancedCriteria.MaxClearPercentage,
                            IsTeamPick = _advancedCriteria.IsTeamPick,
                            RequireLocked = _advancedCriteria.RequireLocked,
                            RequireSubLevel = _advancedCriteria.RequireSubLevel,
                            RequireShareable = _advancedCriteria.RequireShareable,
                            RequiredLabels = new List<string>(_advancedCriteria.RequiredLabels),
                            RequiredTags = new List<string>(_advancedCriteria.RequiredTags),
                            LabelMatchMode = _advancedCriteria.LabelMatchMode,
                            ExcludedLabels = new List<string>(_advancedCriteria.ExcludedLabels),
                            ExcludedTags = new List<string>(_advancedCriteria.ExcludedTags),
                            ExcludedCreators = _advancedCriteria.ExcludedCreators,
                            ExcludedContributors = _advancedCriteria.ExcludedContributors,
                            ExcludedObjectContributors = _advancedCriteria.ExcludedObjectContributors,
                            PublishedBefore = _advancedCriteria.PublishedBefore,
                            PublishedAfter = _advancedCriteria.PublishedAfter,
                            ExcludeTeamPick = _advancedCriteria.ExcludeTeamPick,
                            ExcludeLocked = _advancedCriteria.ExcludeLocked,
                            ExcludeSubLevels = _advancedCriteria.ExcludeSubLevels,
                            ExcludeShareable = _advancedCriteria.ExcludeShareable,
                            MaxHearts = _advancedCriteria.MaxHearts,
                            MaxPlays = _advancedCriteria.MaxPlays
                        }
                    };
                }
                else if (SearchTypeIndex == 1)
                {
                    await PerformUserSearchAsync(keyword, ExactMatch, limitFilter, searchToken, "Found", true);
                    if (UserResultsList.Count > 0) SelectedUser = UserResultsList[0];

                    if (UserResultsList.Count == 0)
                        StatusText = "No creators matched the random search criteria.";
                    else
                        StatusText = "Surprise! Found a random creator.";

                    _currentSearch = new SearchState
                    {
                        SearchText = keyword, SearchTypeIndex = 1, LimitIndex = LimitIndex, Exact = ExactMatch,
                        SelectedUser = SelectedUser,
                        IsSurpriseMe = true,
                        AdvancedCriteria = new AdvancedSearchCriteria()
                    };
                }
            }
            catch (OperationCanceledException) { StatusText = "Search cancelled."; }
            catch (Exception ex)
            {
                _viewService.Alert($"Database Error: {ex.Message}", "Error");
                StatusText = "Search failed.";
            }
            finally
            {
                IsSearching = false;
                IsProgressVisible = Visibility.Hidden;
                IsProgressIndeterminate = false;
            }
        }

        private async Task PerformLevelSearchAsync(string keyword, bool exact, bool searchDesc, int gameFilter, string? genreFilter, string? limitFilter, AdvancedSearchCriteria criteria, CancellationToken token, string statusPrefix, bool searchContributions, bool searchObjects)
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ResultsList);
            view.SortDescriptions.Clear();
            
            if (limitFilter == "All")
            {
                view.SortDescriptions.Add(new System.ComponentModel.SortDescription("Hearts", System.ComponentModel.ListSortDirection.Descending));
            }

            int count = 0;
            var sw = Stopwatch.StartNew();
            var progressReporter = new Progress<string>(status => StatusText = status);

            await Task.Run(async () =>
            {
                var buffer = new List<LevelItem>();
                await foreach (var lvl in _dbService.SearchLevelsAsync(keyword, exact, searchDesc, gameFilter, genreFilter, limitFilter, _savedLevels.ToHashSet(), HeartedLevelsManager.HeartedLevels.Select(x => x.Id).ToHashSet(), criteria, progressReporter, searchContributions, searchObjects, false, token).ConfigureAwait(false))
                {
                    buffer.Add(lvl);
                    count++;

                    if (sw.ElapsedMilliseconds > 500)
                    {
                        var chunk = buffer.ToList();
                        buffer.Clear();
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            ResultsList.AddRange(chunk);
                            StatusText = string.IsNullOrEmpty(keyword) ? $"{statusPrefix} {count} levels..." : $"{statusPrefix} {count} levels for '{keyword}'...";
                        }, System.Windows.Threading.DispatcherPriority.Background);
                        sw.Restart();
                    }
                }
                if (buffer.Count > 0)
                {
                    var chunk = buffer.ToList();
                    await Application.Current.Dispatcher.InvokeAsync(() => { ResultsList.AddRange(chunk); });
                }
            });

            IsProgressIndeterminate = false;
            ProgressMaximum = count;
            ProgressValue = count;
            StatusText = string.IsNullOrEmpty(keyword) ? $"{statusPrefix} {count} levels." : $"{statusPrefix} {count} levels for '{keyword}'.";
        }

        private async Task PerformUserSearchAsync(string keyword, bool exact, string? limitFilter, CancellationToken token, string statusPrefix, bool randomSingle = false)
        {
            var results = await _dbService.SearchUsersAsync(keyword, exact, limitFilter, randomSingle, token);
            IsProgressIndeterminate = false;
            ProgressMaximum = results.Count;
            ProgressValue = results.Count;

            UserResultsList = results;
            StatusText = string.IsNullOrEmpty(keyword) ? $"{statusPrefix} {results.Count} creators." : $"{statusPrefix} {results.Count} creators matching '{keyword}'.";
        }

        public void InitiateCreatorSearch(string npHandle)
        {
            SearchText = npHandle;
            ExactMatch = true;
            SearchDesc = false;
            GameIndex = 0;
            SelectedGenre = "All Genres";
            _advancedCriteria = new AdvancedSearchCriteria();
            if (SearchTypeIndex == 0) SearchCommand.Execute(null); else SearchTypeIndex = 0;
        }

        public void InitiateUserSearch(string npHandle)
        {
            SearchText = npHandle;
            ExactMatch = true;
            SearchDesc = false;
            GameIndex = 0;
            SelectedGenre = "All Genres";
            _advancedCriteria = new AdvancedSearchCriteria();
            if (SearchTypeIndex == 1) SearchCommand.Execute(null); else SearchTypeIndex = 1;
        }

        public void InitiateContributionsSearch(string npHandle)
        {
            SearchText = npHandle;
            ExactMatch = true;
            SearchDesc = false;
            GameIndex = 0;
            SelectedGenre = "All Genres";
            _advancedCriteria = new AdvancedSearchCriteria();
            if (SearchTypeIndex == 2) SearchCommand.Execute(null); else SearchTypeIndex = 2;
        }

        public void InitiateObjectsSearch(string npHandle)
        {
            SearchText = npHandle;
            ExactMatch = true;
            SearchDesc = false;
            GameIndex = 0;
            SelectedGenre = "All Genres";
            _advancedCriteria = new AdvancedSearchCriteria();
            if (SearchTypeIndex == 3) SearchCommand.Execute(null); else SearchTypeIndex = 3;
        }

        private void NavigateBack()
        {
            if (_searchHistory.Count > 0 && _currentSearch != null)
            {
                if (IsLevelSearch) _currentSearch.SelectedItem = SelectedLevel; else _currentSearch.SelectedUser = SelectedUser;
                PushToHistory(_forwardHistory, _currentSearch);
                ApplySearchState(_searchHistory.Pop());
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void NavigateForward()
        {
            if (_forwardHistory.Count > 0 && _currentSearch != null)
            {
                if (IsLevelSearch) _currentSearch.SelectedItem = SelectedLevel; else _currentSearch.SelectedUser = SelectedUser;
                PushToHistory(_searchHistory, _currentSearch);
                ApplySearchState(_forwardHistory.Pop());
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private static void PushToHistory(Stack<SearchState> stack, SearchState state)
        {
            stack.Push(state);
            while (stack.Count > 10)
            {
                var temp = stack.ToArray();
                stack.Clear();
                for (int i = temp.Length - 2; i >= 0; i--) stack.Push(temp[i]);
            }
        }

        private async void ApplySearchState(SearchState state)
        {
            _isApplyingState = true;
            try
            {
                _currentSearch = state;
                SearchTypeIndex = state.SearchTypeIndex;
                SearchText = state.SearchText;
                _advancedCriteria = state.AdvancedCriteria;
                GameIndex = state.GameIndex;
                SelectedGenre = state.Genre;
                LimitIndex = state.LimitIndex;
                ExactMatch = state.Exact;
                SearchDesc = state.SearchDesc;
            }
            finally { _isApplyingState = false; }

            bool hasAdv = state.AdvancedCriteria.MinHearts > 0 || state.AdvancedCriteria.MinPlays > 0 || state.AdvancedCriteria.MinHeartPercentage > 0 || state.AdvancedCriteria.MinYayPercentage > 0 || state.AdvancedCriteria.MinClearPercentage > 0 || state.AdvancedCriteria.MaxClearPercentage > 0 || state.AdvancedCriteria.IsTeamPick || state.AdvancedCriteria.RequireLocked || state.AdvancedCriteria.RequireSubLevel || state.AdvancedCriteria.RequireShareable || state.AdvancedCriteria.ExcludeTeamPick || state.AdvancedCriteria.ExcludeLocked || state.AdvancedCriteria.ExcludeSubLevels || state.AdvancedCriteria.ExcludeShareable || state.AdvancedCriteria.RequiredLabels.Count > 0 || state.AdvancedCriteria.RequiredTags.Count > 0;
            if (string.IsNullOrWhiteSpace(state.SearchText) && state.LimitIndex == 4 && !hasAdv && !state.IsSurpriseMe)
            {
                StatusText = "Previous search was too broad and will not be restored.";
                _currentSearch = null;
                ConfigManager.LastSearch = null;
                return;
            }

            IsSearching = true;
            StatusText = "Restoring search...";
            IsProgressVisible = Visibility.Visible;
            IsProgressIndeterminate = true;

            ResultsList.Clear();
            UserResultsList = new List<UserItem>();

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            try
            {
                string? limitFilter = LimitIndex == 4 ? "All" : (LimitIndex == 3 ? "1000" : (LimitIndex == 2 ? "500" : (LimitIndex == 1 ? "200" : "100")));
                
                if (state.IsSurpriseMe && state.SelectedItem != null && (state.SearchTypeIndex == 0 || state.SearchTypeIndex == 2 || state.SearchTypeIndex == 3))
                {
                    ResultsList.Add(state.SelectedItem);
                    UpdateLevelSavedString(state.SelectedItem);
                    SelectedLevel = state.SelectedItem;
                    StatusText = "Restored random level.";
                }
                else if (state.IsSurpriseMe && state.SelectedUser != null && state.SearchTypeIndex == 1)
                {
                    UserResultsList = new List<UserItem> { state.SelectedUser };
                    SelectedUser = state.SelectedUser;
                    StatusText = "Restored random creator.";
                }
                else if (IsLevelSearch)
                {
                    await PerformLevelSearchAsync(state.SearchText, state.Exact, state.SearchDesc, state.GameIndex, state.Genre, limitFilter, state.AdvancedCriteria, _searchCts.Token, "Restored", state.SearchTypeIndex == 2, state.SearchTypeIndex == 3);
                    var view = System.Windows.Data.CollectionViewSource.GetDefaultView(ResultsList);
                    var viewFirst = view.Cast<LevelItem>().FirstOrDefault();
                    SelectedLevel = state.SelectedItem != null ? ResultsList.FirstOrDefault(x => x.Id == state.SelectedItem.Id) ?? viewFirst : viewFirst;
                }
                else
                {
                    await PerformUserSearchAsync(state.SearchText, state.Exact, limitFilter, _searchCts.Token, "Restored", false);
                    var view = System.Windows.Data.CollectionViewSource.GetDefaultView(UserResultsList);
                    var viewFirst = view.Cast<UserItem>().FirstOrDefault();
                    SelectedUser = state.SelectedUser != null ? UserResultsList.FirstOrDefault(x => x.NpHandle == state.SelectedUser.NpHandle) ?? viewFirst : viewFirst;
                }
            }
            catch (OperationCanceledException) { StatusText = "Search cancelled."; }
            catch (Exception) { StatusText = "Failed to restore search."; }
            finally { IsSearching = false; IsProgressVisible = Visibility.Hidden; }
        }

        private async Task ExtractLevelsAsync(IList<LevelItem> levels)
        {
            var window = _viewService.GetMainWindow();
            await LevelExtractionService.ExtractLevelsAsync(window, levels.ToList(), lvl =>
            {
                _savedLevels.Add(lvl.Id);
                var existingItem = ResultsList.FirstOrDefault(x => x.Id == lvl.Id);
                if (existingItem != null) UpdateLevelSavedString(existingItem);
                UpdateLevelSavedString(lvl);
            });
            StatusText = "Batch extraction finished.";
        }

        public async Task BatchDownloadAsync(UserItem selectedUser)
        {
            if (_viewService.Confirm($"Are you sure you want to download all {selectedUser.TotalLevels} levels by {selectedUser.NpHandle}?\nThis may take a while.", "Confirm Batch Download"))
            {
                StatusText = $"Fetching levels for {selectedUser.NpHandle}...";
                var creatorLevels = new List<LevelItem>();
                var progressReporter = new Progress<string>(status => StatusText = status);

                await Task.Run(async () =>
                {
                    await foreach (var lvl in _dbService.SearchLevelsAsync(selectedUser.NpHandle, true, false, 0, "All Genres", "All", _savedLevels.ToHashSet(), HeartedLevelsManager.HeartedLevels.Select(x => x.Id).ToHashSet(), new AdvancedSearchCriteria(), progressReporter).ConfigureAwait(false))
                        creatorLevels.Add(lvl);
                });

                var strictlyCreatorLevels = creatorLevels.Where(l => l.Creator != null && l.Creator.Equals(selectedUser.NpHandle, StringComparison.OrdinalIgnoreCase)).ToList();

                if (!strictlyCreatorLevels.Any())
                {
                    _viewService.Alert("Could not find any levels for this creator.", "Notice");
                    StatusText = "No levels found.";
                    return;
                }

                await ExtractLevelsAsync(strictlyCreatorLevels);
            }
        }
    }
}