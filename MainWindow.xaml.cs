using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using LbpArchiveToolkit.Utils;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Threading;

namespace LbpArchiveToolkit
{
    public partial class MainWindow : Window
    {
        #region State & Dependencies

        private CancellationTokenSource? _iconCts;
        private CancellationTokenSource? _selectionCts;
        private CancellationTokenSource? _userSelectionCts;

        private DatabaseService _dbService;
        private List<LevelItem> _resultsList = new();
        private List<UserItem> _userResultsList = new();
        private readonly HashSet<long> _savedLevels = new();
        
        private readonly Stack<SearchState> _searchHistory = new();
        private readonly Stack<SearchState> _forwardHistory = new();
        private SearchState? _currentSearch = null;
        
        private AdvancedSearchCriteria _advancedCriteria = new();

        private long _currentIconRequestId = -1;

        internal static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
{
    MaxConnectionsPerServer = 10,
    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    ConnectTimeout = TimeSpan.FromSeconds(15) // Fails fast if a proxy/server stalls on connection
}) 
{ 
    Timeout = TimeSpan.FromMinutes(5) 
};

        [GeneratedRegex(@"(\@[a-zA-Z0-9_-]+)")]
        private static partial Regex MentionRegex();

        public class SearchState
        {
            public string SearchText { get; set; } = "";
            public int SearchTypeIndex { get; set; } = 0;
            public int GameIndex { get; set; }
            public AdvancedSearchCriteria AdvancedCriteria { get; set; } = new();
            public string Genre { get; set; } = "All Genres";
            public int LimitIndex { get; set; }
            public bool Exact { get; set; }
            public bool SearchDesc { get; set; }
            public LevelItem? SelectedItem { get; set; }
            public UserItem? SelectedUser { get; set; }
        }

        #endregion

        #region Initialization & Lifecycle

        public MainWindow()
        {
            InitializeComponent();
                        
            ConfigManager.LoadConfig();
            SavedLevelsManager.Load(ConfigManager.LegacySavedLevels);
            HeartedLevelsManager.Load();
            HeartedCreatorsManager.Load();

            LbpArchiveToolkit.Themes.ThemeManager.ApplyTheme(ConfigManager.Theme);
            
            _dbService = new DatabaseService(ConfigManager.DatabasePath);

            RestoreWindowPosition();
            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
            this.SourceInitialized += (s, e) =>
            {
                if (ConfigManager.IsMaximized)
                {
                    this.WindowState = WindowState.Maximized;
                }
            };

            dgResults.ItemsSource = _resultsList;
            dgUsers.ItemsSource = _userResultsList;
            SharedHttpClient.DefaultRequestHeaders.Add("User-Agent", "LbpArchiveToolkit/1.0");

            _ = LoadSavedLevelsAsync();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowPosition();

            _iconCts?.Cancel();
            _iconCts?.Dispose();
            _iconCts = null;

            _selectionCts?.Cancel();
            _selectionCts?.Dispose();
            _selectionCts = null;

            _userSelectionCts?.Cancel();
            _userSelectionCts?.Dispose();
            _userSelectionCts = null;

            if (_currentSearch != null)
            {
                if (_currentSearch.SearchTypeIndex == 0)
                    _currentSearch.SelectedItem = dgResults.SelectedItem as LevelItem;
                else
                    _currentSearch.SelectedUser = dgUsers.SelectedItem as UserItem;

                ConfigManager.LastSearch = _currentSearch;
            }

            ConfigManager.SaveConfig();
            base.OnClosing(e);
        }

        private void RestoreWindowPosition()
        {
            bool hasSavedLocation = ConfigManager.WindowLeft != -1 && ConfigManager.WindowTop != -1;

            if (ConfigManager.WindowWidth > 0 && ConfigManager.WindowHeight > 0 && hasSavedLocation)
            {
                this.Width = ConfigManager.WindowWidth;
                this.Height = ConfigManager.WindowHeight;

                double virtualLeft = SystemParameters.VirtualScreenLeft;
                double virtualTop = SystemParameters.VirtualScreenTop;
                double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
                double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

                bool isOffScreen = 
                    ConfigManager.WindowLeft >= virtualRight ||
                    ConfigManager.WindowTop >= virtualBottom ||
                    (ConfigManager.WindowLeft + ConfigManager.WindowWidth) <= virtualLeft ||
                    (ConfigManager.WindowTop + ConfigManager.WindowHeight) <= virtualTop;

                if (!isOffScreen)
                {
                    this.WindowStartupLocation = WindowStartupLocation.Manual; 
                    this.Left = ConfigManager.WindowLeft;
                    this.Top = ConfigManager.WindowTop;
                }
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadGenresAsync();
            
            if (ConfigManager.LastSearch != null)
            {
                ApplySearchState(ConfigManager.LastSearch);
            }
            
            await CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            if ((DateTime.Now - ConfigManager.LastUpdateCheck).TotalHours < 12)
                return;

            try
            {
                ConfigManager.LastUpdateCheck = DateTime.Now;
                string url = "https://api.github.com/repos/GigsTheCat/LBP_Archive_Toolkit/releases/latest";
                var response = await SharedHttpClient.GetStringAsync(url);
                var json = JsonNode.Parse(response);
                string? tag = json?["tag_name"]?.ToString();
                 
                if (!string.IsNullOrEmpty(tag))
                {
                    string versionStr = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
                    if (Version.TryParse(versionStr, out Version? latestVersion))
                     {
                         var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                         if (currentVersion != null && latestVersion > currentVersion)
                         {
                             if (!this.IsVisible) return;
                             
                             bool update = CustomDialog.Show(this, "A new version of LBP Archive Toolkit is available.\n\nWould you like to download it now?", "Update Available", isYesNo: true);
                             if (update)
                             {
                                 Process.Start(new ProcessStartInfo("https://github.com/GigsTheCat/LBP_Archive_Toolkit/releases") { UseShellExecute = true });
                             }
                         }
                     }
                 }
             }
             catch { }
         }

        private void SaveWindowPosition()
        {
            ConfigManager.IsMaximized = (this.WindowState == WindowState.Maximized);

            if (this.WindowState == WindowState.Normal)
            {
                ConfigManager.WindowWidth = this.Width;
                ConfigManager.WindowHeight = this.Height;
                ConfigManager.WindowLeft = this.Left;
                ConfigManager.WindowTop = this.Top;
            }
            else
            {
                ConfigManager.WindowWidth = this.RestoreBounds.Width;
                ConfigManager.WindowHeight = this.RestoreBounds.Height;
                ConfigManager.WindowLeft = this.RestoreBounds.Left;
                ConfigManager.WindowTop = this.RestoreBounds.Top;
            }
        }

        private async Task LoadGenresAsync()
        {
            try
            {
                var genres = await _dbService.GetGenresAsync();

                Dispatcher.Invoke(() =>
                {
                    if (Application.Current == null || Application.Current.MainWindow == null) return;
                    
                    cmbGenre.Items.Clear();
                    cmbGenre.Items.Add(new ComboBoxItem { Content = "All Genres" });
                    foreach (var g in genres.OrderBy(x => x))
                    {
                        cmbGenre.Items.Add(new ComboBoxItem { Content = g });
                    }
                    cmbGenre.SelectedIndex = 0;
                });
            }
            catch { }
        }

        #endregion

        #region Custom Title Bar Controls

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) 
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        #endregion

        #region UI Event Handlers & Menus

        private void MenuHeartedCreators_Click(object sender, RoutedEventArgs e)
        {
            var heartedWin = new HeartedCreatorsWindow { Owner = this };
            heartedWin.ShowDialog();
            
            RefreshCurrentUserSelectionHeartState();
        }

        private async void HeartCreatorContext_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItem is LevelItem selected && !string.IsNullOrEmpty(selected.Creator))
            {
                string creatorName = selected.Creator;
                
                if (HeartedCreatorsManager.IsHearted(creatorName))
                {
                    HeartedCreatorsManager.Remove(creatorName);
                    CustomDialog.Show(this, $"{creatorName} has been removed from your hearted creators.", "Removed", false);
                }
                else
                {
                    var users = await _dbService.SearchUsersAsync(creatorName, true, "1");
                    var userToHeart = users.FirstOrDefault(u => u.NpHandle.Equals(creatorName, StringComparison.OrdinalIgnoreCase));
                    
                    if (userToHeart == null)
                    {
                        userToHeart = new UserItem { NpHandle = creatorName };
                    }
                    
                    HeartedCreatorsManager.Add(userToHeart);
                    CustomDialog.Show(this, $"{creatorName} has been added to your hearted creators!", "Hearted", false);
                }
            }
           
        }

        private void CreatorContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu menu)
            {
                string? creatorName = null;
                
                // ContextMenus exist outside the standard visual tree, so we safely resolve the DataContext
                if (menu.DataContext is LevelItem level1) 
                    creatorName = level1.Creator;
                else if (menu.PlacementTarget is FrameworkElement fe && fe.DataContext is LevelItem level2) 
                    creatorName = level2.Creator;

                if (!string.IsNullOrEmpty(creatorName))
                {
                    bool isHearted = HeartedCreatorsManager.IsHearted(creatorName);
                    
                    // Iterate to find the specific menu item by its x:Name instead of its Header string
                    foreach (var item in menu.Items)
                    {
                        if (item is MenuItem menuItem && menuItem.Name == "MenuHeartCreator")
                        {
                            menuItem.Header = isHearted ? "Unheart Creator" : "Heart Creator";
                            break;
                        }
                    }
                }
            }
        }

        private void BtnUserHeartToggle_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem is UserItem selectedUser)
            {
                bool isHearted = HeartedCreatorsManager.IsHearted(selectedUser.NpHandle);
                if (isHearted)
                {
                    HeartedCreatorsManager.Remove(selectedUser.NpHandle);
                }
                else
                {
                    HeartedCreatorsManager.Add(selectedUser);
                }
                RefreshCurrentUserSelectionHeartState();
            }
        }

        private void RefreshCurrentUserSelectionHeartState()
        {
            if (dgUsers.SelectedItem is UserItem selectedUser)
            {
                bool isHearted = HeartedCreatorsManager.IsHearted(selectedUser.NpHandle);
                btnUserHeartToggle.Content = isHearted ? "♡ UNHEART CREATOR" : "♥ HEART CREATOR";
                userIconHeartOverlay.Visibility = isHearted ? Visibility.Visible : Visibility.Hidden;
            }
        }

        public void InitiateCreatorSearch(string npHandle)
        {
            txtSearch.Text = npHandle;
            chkExact.IsChecked = true;
            chkSearchDesc.IsChecked = false;
            cmbGame.SelectedIndex = 0;
            cmbGenre.SelectedIndex = 0;
            _advancedCriteria = new AdvancedSearchCriteria(); 

            cmbSearchType.SelectedIndex = 0; 
        }

        public async Task InitiateBatchDownloadAsync(UserItem selectedUser)
        {
            bool isConfirmed = CustomDialog.Show(
                this, 
                $"Are you sure you want to download all {selectedUser.TotalLevels} levels by {selectedUser.NpHandle}?\nThis may take a while.", 
                "Confirm Batch Download", 
                isYesNo: true);

            if (isConfirmed)
            {
                txtStatus.Text = $"Fetching levels for {selectedUser.NpHandle}...";
                
                var savedLevelsSnapshot = _savedLevels.ToHashSet();
                var heartedLevelsSnapshot = HeartedLevelsManager.HeartedLevels.Select(x => x.Id).ToHashSet();

                var creatorLevels = new List<LevelItem>();
                var progressReporter = new Progress<string>(status =>
                {
                    txtStatus.Text = status;
                });

                await Task.Run(async () =>
                {
                    await foreach (var lvl in _dbService.SearchLevelsAsync(
                        selectedUser.NpHandle, 
                        exact: true, 
                        searchDesc: false, 
                        gameFilter: 0, 
                        genreFilter: "All Genres", 
                        limitFilter: "All", 
                        savedLevelsSnapshot, 
                        heartedLevelsSnapshot, 
                        new AdvancedSearchCriteria(),
                        progressReporter).ConfigureAwait(false))
                    {
                        creatorLevels.Add(lvl);
                    }
                });
                
                var strictlyCreatorLevels = creatorLevels
                    .Where(l => l.Creator != null && l.Creator.Equals(selectedUser.NpHandle, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (!strictlyCreatorLevels.Any())
                {
                    CustomDialog.Show(this, "Could not find any levels for this creator.", "Notice", false);
                    txtStatus.Text = "No levels found.";
                    return;
                }

                await ExtractLevelsAsync(strictlyCreatorLevels);
            }
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

        private void MenuHeartedLevels_Click(object sender, RoutedEventArgs e)
        {
            var heartedWin = new HeartedLevelsWindow { Owner = this };
            heartedWin.ShowDialog();
            
            RefreshCurrentSelectionHeartState();
            
            // Refresh grid visual states in case we unhearted items from the sub-window
            foreach (var item in _resultsList)
            {
                UpdateLevelSavedString(item);
            }
        }

        private void RefreshCurrentSelectionHeartState()
{
    if (dgResults.SelectedItem is LevelItem selectedLevel)
    {
        bool isHearted = HeartedLevelsManager.IsHearted(selectedLevel.Id);
        btnHeartToggle.Content = isHearted ? "♡ UNHEART LEVEL" : "♥ HEART LEVEL";
        iconHeartOverlay.Visibility = isHearted ? Visibility.Visible : Visibility.Hidden;
    }
}

private void BtnHeartToggle_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItem is LevelItem selectedLevel)
            {
                bool isHearted = HeartedLevelsManager.IsHearted(selectedLevel.Id);
                if (isHearted)
                {
                    HeartedLevelsManager.Remove(selectedLevel.Id);
                }
                else
                {
                    HeartedLevelsManager.Add(selectedLevel);
                }
                RefreshCurrentSelectionHeartState();
                UpdateLevelSavedString(selectedLevel);
            }
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWin = new SettingsWindow { Owner = this };
            if (settingsWin.ShowDialog() == true)
            {
                _dbService = new DatabaseService(ConfigManager.DatabasePath);
                txtStatus.Text = "Config saved successfully.";
                _ = LoadGenresAsync();
            }
        }

        private void MenuBackupManager_Click(object sender, RoutedEventArgs e)
        {
            var backupWin = new BackupManagerWindow { Owner = this };
            backupWin.ShowDialog();
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var aboutWin = new AboutWindow { Owner = this };
            aboutWin.ShowDialog();
        }

        private void BtnCopyHash_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItem is LevelItem selected && !string.IsNullOrEmpty(selected.Hash))
            {
                Clipboard.SetText(selected.Hash);
                CustomDialog.Show(this, $"Level hash '{selected.Hash}' copied to clipboard!", "Copied", false);
            }
        }

        private void CopyLevelNameContext_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItem is LevelItem selected && !string.IsNullOrEmpty(selected.LevelName))
            {
                Clipboard.SetText(selected.LevelName);
                CustomDialog.Show(this, $"Level name '{selected.LevelName}' copied to clipboard!", "Copied", false);
            }
        }

        private void SearchCreatorContext_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItem is LevelItem selected)
            {
                txtSearch.Text = selected.Creator;
                cmbSearchType.SelectedIndex = 1; // Switch UI to Creators, CmbSearchType_SelectionChanged triggers search
            }
        }

        private void CmbSearchType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbSearchType == null) return;
            bool isLevels = cmbSearchType.SelectedIndex == 0;

            panelLevelFilters?.Visibility = isLevels ? Visibility.Visible : Visibility.Collapsed;
            chkSearchDesc?.Visibility = isLevels ? Visibility.Visible : Visibility.Collapsed;
            btnAdvanced?.Visibility = isLevels ? Visibility.Visible : Visibility.Collapsed;

            dgResults?.Visibility = isLevels ? Visibility.Visible : Visibility.Collapsed;
            dgUsers?.Visibility = isLevels ? Visibility.Collapsed : Visibility.Visible;

            panelLevelDetails?.Visibility = isLevels ? Visibility.Visible : Visibility.Collapsed;
            panelUserDetails?.Visibility = isLevels ? Visibility.Collapsed : Visibility.Visible;

            if (this.IsLoaded && !_isApplyingState && txtSearch != null && !string.IsNullOrWhiteSpace(txtSearch.Text))
            {
                BtnSearch_Click(sender, null!);
            }
        }

        #endregion

        #region Search & Navigation Logic

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            string keyword = txtSearch.Text.Trim();

            SetUIState(isSearching: true);
            txtStatus.Text = "Searching database...";
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;

            if (_currentSearch != null)
            {
                // Save the currently selected item BEFORE we clear the DataGrid
                if (_currentSearch.SearchTypeIndex == 0)
                    _currentSearch.SelectedItem = dgResults.SelectedItem as LevelItem;
                else
                    _currentSearch.SelectedUser = dgUsers.SelectedItem as UserItem;

                PushToHistory(_searchHistory, _currentSearch);
            }

            // Clear previous results to avoid old icons flashing
            _resultsList = new List<LevelItem>();
            dgResults.ItemsSource = _resultsList;
            
            _userResultsList = new List<UserItem>();
            dgUsers.ItemsSource = _userResultsList;

            _forwardHistory.Clear();
            btnForward.IsEnabled = false;

            int searchType = cmbSearchType.SelectedIndex;
            bool exact = chkExact.IsChecked == true;
            bool searchDesc = chkSearchDesc.IsChecked == true;
            int gameFilter = cmbGame.SelectedIndex;
            int limitFilterIdx = cmbLimit.SelectedIndex;
            string? genreFilter = (cmbGenre.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string? limitFilter = (cmbLimit.SelectedItem as ComboBoxItem)?.Content?.ToString();

            var savedLevelsSnapshot = _savedLevels.ToHashSet();
            var heartedLevelsSnapshot = HeartedLevelsManager.HeartedLevels.Select(x => x.Id).ToHashSet();

            try
            {
                if (searchType == 0)
                {
                    _resultsList = new List<LevelItem>();
                    dgResults.ItemsSource = _resultsList;
                    
                    int count = 0;
                    var sw = Stopwatch.StartNew();

                    var progressReporter = new Progress<string>(status =>
                    {
                        txtStatus.Text = status;
                    });

                    await Task.Run(async () =>
                    {
                        var buffer = new List<LevelItem>();
                        await foreach (var lvl in _dbService.SearchLevelsAsync(keyword, exact, searchDesc, gameFilter, genreFilter, limitFilter, savedLevelsSnapshot, heartedLevelsSnapshot, _advancedCriteria, progressReporter).ConfigureAwait(false))
                        {
                            buffer.Add(lvl);
                            count++;

                            
                            if (sw.ElapsedMilliseconds > 500)
                            {
                                var chunk = buffer.ToList();
                                buffer.Clear();

                                // Use Background priority so the UI rendering doesn't steal CPU time from the DB reader
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    _resultsList.AddRange(chunk);
                                    dgResults.Items.Refresh();
                                    txtStatus.Text = string.IsNullOrEmpty(keyword) ? $"Found {count} levels..." : $"Found {count} levels for '{keyword}'...";
                                }, System.Windows.Threading.DispatcherPriority.Background);
                                
                                sw.Restart();
                            }
                        }

                        // Flush any remaining items to the UI once complete
                        if (buffer.Count > 0)
                        {
                            var chunk = buffer.ToList();
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                _resultsList.AddRange(chunk);
                            });
                        }
                    });
                     
                    progressBar.IsIndeterminate = false;
                    progressBar.Maximum = count;
                    progressBar.Value = count; 

                    dgResults.Items.Refresh();
                    txtStatus.Text = string.IsNullOrEmpty(keyword) ? $"Found {count} levels." : $"Found {count} levels for '{keyword}'.";

                    if (_resultsList.Any())
                    {
                        dgResults.SelectedIndex = 0;
                        dgResults.ScrollIntoView(_resultsList[0]);
                    }

                    _currentSearch = new SearchState
                    {
                        SearchText = keyword,
                        SearchTypeIndex = 0,
                        GameIndex = gameFilter,
                        Genre = genreFilter ?? "All Genres",
                        LimitIndex = limitFilterIdx,
                        Exact = exact,
                        SearchDesc = searchDesc,
                        AdvancedCriteria = new AdvancedSearchCriteria 
                        { 
                            MinHearts = _advancedCriteria.MinHearts,
                            MinPlays = _advancedCriteria.MinPlays,
                            IsTeamPick = _advancedCriteria.IsTeamPick,
                            RequiredLabels = new List<string>(_advancedCriteria.RequiredLabels),
                            RequiredTags = new List<string>(_advancedCriteria.RequiredTags)
                        }
                    };
                }
                else
                {
                    var results = await _dbService.SearchUsersAsync(keyword, exact, limitFilter);

                    progressBar.IsIndeterminate = false;
                    progressBar.Maximum = results.Count;
                    progressBar.Value = results.Count;

                     _userResultsList = results;
                    dgUsers.ItemsSource = _userResultsList;
                    txtStatus.Text = string.IsNullOrEmpty(keyword) ? $"Found {results.Count} creators." : $"Found {results.Count} creators matching '{keyword}'.";

                    if (results.Any())
                    {
                        dgUsers.SelectedIndex = 0;
                        dgUsers.ScrollIntoView(results[0]);
                    }

                    _currentSearch = new SearchState
                    {
                        SearchText = keyword,
                        SearchTypeIndex = 1,
                        LimitIndex = limitFilterIdx,
                        Exact = exact,
                        AdvancedCriteria = new AdvancedSearchCriteria 
                        { 
                            MinHearts = _advancedCriteria.MinHearts,
                            MinPlays = _advancedCriteria.MinPlays,
                            IsTeamPick = _advancedCriteria.IsTeamPick,
                            RequiredLabels = new List<string>(_advancedCriteria.RequiredLabels),
                            RequiredTags = new List<string>(_advancedCriteria.RequiredTags)
                        }
                    };
                }

                btnBack.IsEnabled = _searchHistory.Count > 0;
            }
            catch (FileNotFoundException)
            {
                var missingDbDialog = new MissingDatabaseDialog { Owner = this };
                if (missingDbDialog.ShowDialog() == true)
                {
                    MenuSettings_Click(sender, e); 
                    
                    // If the user successfully linked a valid database in Settings, automatically retry the search
                    if (File.Exists(ConfigManager.DatabasePath))
                    {
                        _ = Application.Current.Dispatcher.InvokeAsync(() => BtnSearch_Click(sender, e));
                        return; 
                    }
                }
                txtStatus.Text = "Search failed. Database missing.";
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, $"Database Error: {ex.Message}", "Error", false);
                txtStatus.Text = "Search failed.";
            }
            finally
            {
                SetUIState(isSearching: false);
                progressBar.Visibility = Visibility.Hidden;
            }
        }

        private void BtnAdvanced_Click(object sender, RoutedEventArgs e)
        {
             var advancedWin = new AdvancedSearchWindow(_advancedCriteria) { Owner = this };
             if (advancedWin.ShowDialog() == true)
             {
                  _advancedCriteria = advancedWin.Criteria;
                  if (advancedWin.ShouldSearch && !string.IsNullOrWhiteSpace(txtSearch.Text))
                  {
                      BtnSearch_Click(btnSearch, null!);
                  }
             }
         }

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnSearch_Click(sender, null!);
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_searchHistory.Count > 0 && _currentSearch != null)
            {
                if (_currentSearch.SearchTypeIndex == 0)
                    _currentSearch.SelectedItem = dgResults.SelectedItem as LevelItem;
                else
                    _currentSearch.SelectedUser = dgUsers.SelectedItem as UserItem;

                PushToHistory(_forwardHistory, _currentSearch);
                ApplySearchState(_searchHistory.Pop());
            }

            btnBack.IsEnabled = _searchHistory.Count > 0;
            btnForward.IsEnabled = _forwardHistory.Count > 0;
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (_forwardHistory.Count > 0 && _currentSearch != null)
            {
                if (_currentSearch.SearchTypeIndex == 0)
                    _currentSearch.SelectedItem = dgResults.SelectedItem as LevelItem;
                else
                    _currentSearch.SelectedUser = dgUsers.SelectedItem as UserItem;

                PushToHistory(_searchHistory, _currentSearch);
                ApplySearchState(_forwardHistory.Pop());
            }

            btnBack.IsEnabled = _searchHistory.Count > 0;
            btnForward.IsEnabled = _forwardHistory.Count > 0;
        }

        private static void PushToHistory(Stack<SearchState> stack, SearchState state, int maxDepth = 10)
        {
            stack.Push(state);
            while (stack.Count > maxDepth)
            {
                var temp = stack.ToArray(); 
                stack.Clear();

                for (int i = temp.Length - 2; i >= 0; i--)
                {
                    stack.Push(temp[i]);
                }
            }
        }

        private bool _isApplyingState = false;

        private async void ApplySearchState(SearchState state)
        {
            _isApplyingState = true;
            try
            {
                _currentSearch = state;

                cmbSearchType.SelectedIndex = state.SearchTypeIndex;
                txtSearch.Text = state.SearchText;
                _advancedCriteria = state.AdvancedCriteria;
                cmbGame.SelectedIndex = state.GameIndex;
                
                cmbGenre.SelectedIndex = 0;
                foreach (ComboBoxItem item in cmbGenre.Items)
                {
                    if (item.Content?.ToString() == state.Genre)
                    {
                        cmbGenre.SelectedItem = item;
                        break;
                    }
                }
                
                cmbLimit.SelectedIndex = state.LimitIndex;
                chkExact.IsChecked = state.Exact;
                chkSearchDesc.IsChecked = state.SearchDesc;
            }
            finally
            {
                _isApplyingState = false;
            }

            SetUIState(isSearching: true);
            txtStatus.Text = "Restoring search...";
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;

            _resultsList = new List<LevelItem>();
            dgResults.ItemsSource = _resultsList;
            
            _userResultsList = new List<UserItem>();
            dgUsers.ItemsSource = _userResultsList;

            var savedLevelsSnapshot = _savedLevels.ToHashSet();
            var heartedLevelsSnapshot = HeartedLevelsManager.HeartedLevels.Select(x => x.Id).ToHashSet();
            string? limitFilter = (cmbLimit.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string? genreFilter = (cmbGenre.SelectedItem as ComboBoxItem)?.Content?.ToString();

            try
            {
                if (state.SearchTypeIndex == 0)
                {
                    _resultsList = new List<LevelItem>();
                    dgResults.ItemsSource = _resultsList;
                    
                    int count = 0;
                    var sw = Stopwatch.StartNew();
                    
                    var progressReporter = new Progress<string>(status =>
                    {
                        txtStatus.Text = status;
                    });

                    await Task.Run(async () =>
                    {
                        var buffer = new List<LevelItem>();
                        await foreach (var lvl in _dbService.SearchLevelsAsync(state.SearchText, state.Exact, state.SearchDesc, state.GameIndex, genreFilter, limitFilter, savedLevelsSnapshot, heartedLevelsSnapshot, state.AdvancedCriteria, progressReporter).ConfigureAwait(false))
                        {
                            buffer.Add(lvl);
                            count++;

                            if (sw.ElapsedMilliseconds > 500)
                            {
                                var chunk = buffer.ToList();
                                buffer.Clear();

                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    _resultsList.AddRange(chunk);
                                    dgResults.Items.Refresh();
                                    txtStatus.Text = string.IsNullOrEmpty(state.SearchText) ? $"Restored {count} levels..." : $"Restored {count} levels for '{state.SearchText}'...";
                                }, System.Windows.Threading.DispatcherPriority.Background);
                                
                                sw.Restart();
                            }
                        }

                        if (buffer.Count > 0)
                        {
                            var chunk = buffer.ToList();
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                _resultsList.AddRange(chunk);
                            });
                        }
                    });
                    
                    progressBar.IsIndeterminate = false;
                    progressBar.Maximum = count;
                    progressBar.Value = count; 

                    dgResults.Items.Refresh();
                    txtStatus.Text = string.IsNullOrEmpty(state.SearchText) ? $"Restored {count} levels." : $"Restored {count} levels for '{state.SearchText}'.";

                    dgResults.SelectedItem = null;
                    if (state.SelectedItem != null)
                    {
                        var itemToSelect = _resultsList.FirstOrDefault(x => x.Id == state.SelectedItem.Id);
                        if (itemToSelect != null)
                        {
                            dgResults.SelectedItem = itemToSelect;
                            dgResults.UpdateLayout();
                            dgResults.ScrollIntoView(itemToSelect);
                        }
                    }
                    if (dgResults.SelectedItem == null && _resultsList.Any())
                    {
                        dgResults.SelectedIndex = 0;
                        dgResults.UpdateLayout();
                        dgResults.ScrollIntoView(_resultsList[0]);
                    }
                }
                else
                {
                    var results = await _dbService.SearchUsersAsync(state.SearchText, state.Exact, limitFilter);
                    progressBar.IsIndeterminate = false;
                    progressBar.Maximum = results.Count;
                    progressBar.Value = results.Count;

                    _userResultsList = results;
                    dgUsers.ItemsSource = _userResultsList;
                    txtStatus.Text = string.IsNullOrEmpty(state.SearchText) ? $"Restored {results.Count} creators." : $"Restored {results.Count} creators matching '{state.SearchText}'.";

                    dgUsers.SelectedItem = null;
                    if (state.SelectedUser != null)
                    {
                        var userToSelect = _userResultsList.FirstOrDefault(x => x.NpHandle == state.SelectedUser.NpHandle);
                        if (userToSelect != null)
                        {
                            dgUsers.SelectedItem = userToSelect;
                            dgUsers.UpdateLayout();
                            dgUsers.ScrollIntoView(userToSelect);
                        }
                    }
                    if (dgUsers.SelectedItem == null && _userResultsList.Any())
                    {
                        dgUsers.SelectedIndex = 0;
                        dgUsers.UpdateLayout();
                        dgUsers.ScrollIntoView(_userResultsList[0]);
                    }
                }
            }
            catch (Exception)
            {
                txtStatus.Text = "Failed to restore search.";
            }
            finally
            {
                SetUIState(isSearching: false);
                progressBar.Visibility = Visibility.Hidden;
            }
        }

        private void SetUIState(bool isSearching)
        {
            txtSearch.IsEnabled = !isSearching;
            btnSearch.IsEnabled = !isSearching;
        }

        #endregion

        #region DataGrid & Details View

        private async void DgResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectionCts?.Cancel();
            _selectionCts?.Dispose();
            _selectionCts = null;

            if (dgResults.SelectedItem is LevelItem selectedLevel)
            {
                _selectionCts = new CancellationTokenSource();
                var token = _selectionCts.Token;

                try
                {
                    await Task.Delay(100, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                txtLevelName.Text = selectedLevel.LevelName;
                txtCreator.Text = $"By: {selectedLevel.Creator}  |  Genre: {selectedLevel.Genre}  |  Plays: {selectedLevel.Plays}  |  ♥ {selectedLevel.Hearts}";
                
                SetDescriptionRichText(selectedLevel.Description);

                btnExtract.IsEnabled = true;
                btnCopyHash.IsEnabled = !string.IsNullOrEmpty(selectedLevel.Hash);

                btnHeartToggle.IsEnabled = true;
                RefreshCurrentSelectionHeartState();

                mmPickTails.Visibility = selectedLevel.IsMmPick ? Visibility.Visible : Visibility.Hidden;
                mmPickRosette.Visibility = selectedLevel.IsMmPick ? Visibility.Visible : Visibility.Hidden;
                mmPickRosetteInner.Visibility = selectedLevel.IsMmPick ? Visibility.Visible : Visibility.Hidden;
                iconEllipse.Stroke = selectedLevel.IsMmPick ? (Brush)FindResource("LbpPink") : (Brush)FindResource("LbpOrange");

                _currentIconRequestId = selectedLevel.Id;
                
                _iconCts?.Cancel();
                _iconCts?.Dispose();
                _iconCts = new CancellationTokenSource();
                
                await LoadIconAsync(selectedLevel.IconHash, _iconCts.Token);
            }
            else
            {
                btnExtract.IsEnabled = false;
                btnCopyHash.IsEnabled = false;
                btnHeartToggle.IsEnabled = false;
                btnHeartToggle.Content = "♥ HEART LEVEL";
                iconHeartOverlay.Visibility = Visibility.Hidden;
                mmPickTails.Visibility = Visibility.Hidden;
                mmPickRosette.Visibility = Visibility.Hidden;
                mmPickRosetteInner.Visibility = Visibility.Hidden;
                iconEllipse.Stroke = (Brush)FindResource("LbpOrange");
                iconEllipse.Fill = (Brush)FindResource("BgPrimary"); 
                txtIconStatus.Text = "Select a level\nto view details";
                
                txtDescription.Document.Blocks.Clear();
                txtLevelName.Text = "";
                txtCreator.Text = "";
            }
        }

        private async void DgUsers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _userSelectionCts?.Cancel();
            _userSelectionCts?.Dispose();
            _userSelectionCts = null;

            if (dgUsers.SelectedItem is UserItem selectedUser)
            {
                _userSelectionCts = new CancellationTokenSource();
                var token = _userSelectionCts.Token;

                try
                {
                    await Task.Delay(100, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                txtUserNpHandle.Text = selectedUser.NpHandle;
                txtUserStats.Text = $"Hearts: {selectedUser.HeartCount}  |  Total Levels: {selectedUser.TotalLevels}";
                txtUserSummary.Text = $"Published Level slots summary:\n" +
                                      $"• LBP1 Slots: {selectedUser.Lbp1UsedSlots}\n" +
                                      $"• LBP2 Slots: {selectedUser.Lbp2UsedSlots}\n" +
                                      $"• LBP3 Slots: {selectedUser.Lbp3UsedSlots}\n\n" +
                                      $"Click the button below to view all levels published by {selectedUser.NpHandle}.";

                btnViewUserLevels.IsEnabled = true;
                btnDownloadAllLevels.IsEnabled = true;
                btnUserHeartToggle.IsEnabled = true;
                RefreshCurrentUserSelectionHeartState();
                _currentIconRequestId = selectedUser.NpHandle.GetHashCode();
                
                _iconCts?.Cancel();
                _iconCts?.Dispose();
                _iconCts = new CancellationTokenSource();
                
                await LoadUserIconAsync(selectedUser.IconHash, selectedUser.NpHandle, _iconCts.Token);
            }
            else
            {
                btnViewUserLevels.IsEnabled = false;
                btnDownloadAllLevels.IsEnabled = false;
                btnUserHeartToggle.IsEnabled = false;
                btnUserHeartToggle.Content = "♥ HEART CREATOR";
                userIconHeartOverlay.Visibility = Visibility.Hidden;
                userIconEllipse.Fill = (Brush)FindResource("BgPrimary"); 
                txtUserIconStatus.Text = "Select a creator\nto view details";
                txtUserNpHandle.Text = "";
                txtUserStats.Text = "";
                txtUserSummary.Text = "";
            }
        }

        private void BtnViewUserLevels_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem is UserItem selectedUser)
            {
                InitiateCreatorSearch(selectedUser.NpHandle);
            }
        }

        private void SetDescriptionRichText(string? text)
        {
            txtDescription.IsDocumentEnabled = true; 
            txtDescription.Document.Blocks.Clear();
            if (string.IsNullOrEmpty(text)) return;

            int lastIndex = 0;
            FlowDocument doc = txtDescription.Document;
            Paragraph para = new Paragraph();

            foreach (var match in MentionRegex().EnumerateMatches(text))
            {
                if (match.Index > lastIndex)
                {
                    para.Inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));
                }

                string mentionStr = text.Substring(match.Index, match.Length);
                Hyperlink link = new Hyperlink(new Run(mentionStr));
                link.Foreground = Brushes.LightBlue;
                link.Cursor = Cursors.Hand;
                
                link.Click += (s, e) =>
                {
                    string name = mentionStr.Substring(1);
                    txtSearch.Text = name;
                    cmbSearchType.SelectedIndex = 1; // Switch UI to Creators, CmbSearchType_SelectionChanged triggers search
                    e.Handled = true;
                };
                para.Inlines.Add(link);

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
            {
                para.Inlines.Add(new Run(text.Substring(lastIndex)));
            }

            doc.Blocks.Add(para);
        }

        private async Task LoadIconAsync(string? hash, CancellationToken token)
        {
            iconEllipse.Fill = (Brush)FindResource("BgPrimary");

            if (string.IsNullOrEmpty(hash) || hash.Length <= 8)
            {
                txtIconStatus.Text = "No Icon Available";
                return;
            }

            txtIconStatus.Text = "Loading Icon...";
            long expectedRequestId = _currentIconRequestId;

            var brush = await LbpArchiveToolkit.Services.IconLoaderService.LoadIconBrushAsync(hash, SharedHttpClient, token);

            if (_currentIconRequestId != expectedRequestId || token.IsCancellationRequested) return;

            if (brush != null)
            {
                iconEllipse.Fill = brush;
                txtIconStatus.Text = "";
            }
            else
            {
                txtIconStatus.Text = "Icon offline\nor missing.";
            }
        }

        private async Task LoadUserIconAsync(string? hash, string npHandle, CancellationToken token)
        {
            userIconEllipse.Fill = (Brush)FindResource("BgPrimary");

            if (string.IsNullOrEmpty(hash) || hash.Length <= 8)
            {
                txtUserIconStatus.Text = "No Icon Available";
                return;
            }

            txtUserIconStatus.Text = "Loading Icon...";
            long expectedRequestId = npHandle.GetHashCode();

            var brush = await LbpArchiveToolkit.Services.IconLoaderService.LoadIconBrushAsync(hash, SharedHttpClient, token);

            if (_currentIconRequestId != expectedRequestId || token.IsCancellationRequested) return;

            if (brush != null)
            {
                userIconEllipse.Fill = brush;
                txtUserIconStatus.Text = "";
            }
            else
            {
                txtUserIconStatus.Text = "Icon offline\nor missing.";
            }
        }

        #endregion

        #region Extraction & Downloading Manager

        private async void BtnExtract_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = dgResults.SelectedItems.Cast<LevelItem>().ToList();
            if (!selectedItems.Any()) return;
            await ExtractLevelsAsync(selectedItems);
        }

        private async void BtnDownloadAllLevels_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem is UserItem selectedUser)
            {
                await InitiateBatchDownloadAsync(selectedUser);
            }
        }
        

        private async Task ExtractLevelsAsync(List<LevelItem> levelsToExtract)
        {
            var progressWin = new ProgressWindow { Owner = this };
            progressWin.Show();
            this.IsEnabled = false;

            int successCount = 0;
            int failureCount = 0;
            bool wasCancelled = false;
            var errorMessages = new List<string>();

            try
            {
                var token = progressWin.CancellationTokenSource.Token;

                for (int i = 0; i < levelsToExtract.Count; i++)
                {
                    if (token.IsCancellationRequested) 
                    {
                        wasCancelled = true;
                        break;
                    }

                    var lvl = levelsToExtract[i];
                    string baseStatus = $"[{i + 1}/{levelsToExtract.Count}] Extracting: {lvl.LevelName}";
                    
                    progressWin.UpdateProgress(0, 1, baseStatus, "Initializing download...");

                    var progressIndicator = new Progress<(int processed, int total, string message)>(report =>
                    {
                        progressWin.UpdateProgress(report.processed, report.total, baseStatus, $"{report.message}\nProgress: {report.processed} / {report.total}");
                    });

                    try
                    {
                        var config = new ExtractionConfig
                        {
                            DownloadServer = ConfigManager.DownloadServer,
                            LocalArchivePath = ConfigManager.LocalArchivePath,
                            MaxParallelDownloads = ConfigManager.MaxParallelDownloads
                        };

                        var result = await AssetDownloader.RunExtractionProcessAsync(lvl, ConfigManager.DatabasePath, ConfigManager.BackupDirectory, SharedHttpClient, config, token, progressIndicator);
                        
                        if (result.Success)
                        {
                            successCount++;
                            _savedLevels.Add(lvl.Id);
                            
                            // Synchronize with visual instances currently active on the UI Thread
                            var existingItem = _resultsList.FirstOrDefault(x => x.Id == lvl.Id);
                            if (existingItem != null) UpdateLevelSavedString(existingItem);
                            
                            UpdateLevelSavedString(lvl);
                            
                            if (!SavedLevelsManager.Contains(lvl.Id.ToString()))
                            {
                                SavedLevelsManager.SavedLevels.Add(lvl.Id.ToString());
                            }
                        }
                        else
                        {
                            if (result.ErrorMessage.Contains("cancelled")) wasCancelled = true;
                            else
                            {
                                failureCount++;
                                errorMessages.Add($"'{lvl.LevelName}': {result.ErrorMessage}");
                            }
                        }
                    }
                    finally
                    {
                        AssetDownloader.CleanupLocalArchives();
                    }
                }
                
                if (successCount > 0)
                {
                    SavedLevelsManager.Save();
                    dgResults.Items.Refresh(); // Refresh datagrid visual icons
                }
            }
            finally
            {
                progressWin.Close();
                this.IsEnabled = true;
                AssetDownloader.CleanupLocalArchives();

                // Force a full garbage collection and compact the Large Object Heap
                // to immediately release memory claimed during massive batch processes.
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
            }

            txtStatus.Text = $"Batch complete. {successCount} packed. {failureCount} failed.";

            if (failureCount > 0)
            {
                string errors = string.Join("\n\n", errorMessages);
                CustomDialog.Show(this, $"Failed to download/pack {failureCount} level(s).\n\nReasons:\n{errors}", "Extraction Failed", false);
            }

            if (successCount > 0)
            {
                string msg = wasCancelled ? $"Cancelled! However, {successCount} level(s) were successfully packed before cancellation.\n\nOpen backup folder?" 
                                          : $"Successfully packed {successCount} level(s)!\n\nOpen backup folder?";

                if (CustomDialog.Show(this, msg, "Finished", true))
                {
                    string fullPath = Path.GetFullPath(ConfigManager.BackupDirectory);
                    if (Directory.Exists(fullPath)) Process.Start("explorer.exe", $"\"{fullPath}\"");
                }
            }
        }

        private async Task LoadSavedLevelsAsync()
        {
            foreach (var levelId in SavedLevelsManager.SavedLevels)
            {
                if (long.TryParse(levelId, out long parsedId))
                {
                    _savedLevels.Add(parsedId);
                }
            }
            
            if (Directory.Exists(ConfigManager.BackupDirectory))
            {
                var discoveredIds = await Task.Run(() =>
                {
                    var ids = new List<long>();
                    foreach (var dir in Directory.EnumerateDirectories(ConfigManager.BackupDirectory))
                    {
                        string dirName = Path.GetFileName(dir);
                        if (dirName.Length >= 8)
                        {
                            string hexId = dirName.Substring(dirName.Length - 8);
                            if (long.TryParse(hexId, System.Globalization.NumberStyles.HexNumber, null, out long id))
                            {
                                ids.Add(id);
                            }
                        }
                    }
                    return ids;
                });
                
                bool needsUpdate = false;
                foreach (var id in discoveredIds)
                {
                    _savedLevels.Add(id);
                    string idStr = id.ToString();
                    if (!SavedLevelsManager.Contains(idStr))
                    {
                        SavedLevelsManager.SavedLevels.Add(idStr);
                        needsUpdate = true;
                    }
                }
                
                if (needsUpdate) SavedLevelsManager.Save();
            }
        }

        public void ClearSavedLevels()
        {
            _savedLevels.Clear();
            SavedLevelsManager.Clear();

            foreach (var item in _resultsList) UpdateLevelSavedString(item);
            
            dgResults.Items.Refresh(); 
        }

        #endregion

           }
}
