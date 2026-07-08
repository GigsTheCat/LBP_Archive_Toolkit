using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LbpArchiveToolkit
{
    public partial class MainWindow : Window
    {
        #region State & Dependencies

        private CancellationTokenSource? _iconCts;
        private CancellationTokenSource? _selectionCts;
        private CancellationTokenSource? _userSelectionCts;
        private CancellationTokenSource? _toastCts;
        private CancellationTokenSource? _searchCts;

        private DatabaseService _dbService;
        private ObservableCollection<LevelItem> _resultsList = new();
        private List<UserItem> _userResultsList = new();
        private readonly HashSet<long> _savedLevels = new();

        private readonly Stack<SearchState> _searchHistory = new();
        private readonly Stack<SearchState> _forwardHistory = new();
        private SearchState? _currentSearch = null;

        private AdvancedSearchCriteria _advancedCriteria = new();

        private long _iconRequestCounter = 0;
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

        static MainWindow()
        {
            SharedHttpClient.DefaultRequestHeaders.Add("User-Agent", "LbpArchiveToolkit/1.0");
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

            // Globally listen for ANY copy events (Ctrl+C or Right Click -> Copy) in the description box
            DataObject.AddCopyingHandler(txtDescription, (s, e) =>
            {
                ShowToast("Copied!", txtDescription);
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowPosition();

            _iconCts?.Cancel();
            _iconCts = null;

            _selectionCts?.Cancel();
            _selectionCts = null;

            _userSelectionCts?.Cancel();
            _userSelectionCts = null;

            var current = _currentSearch;
            if (current != null)
            {
                if (current.SearchTypeIndex == 0 || current.SearchTypeIndex == 2)
                    current.SelectedItem = dgResults.SelectedItem as LevelItem;
                else
                    current.SelectedUser = dgUsers.SelectedItem as UserItem;

                ConfigManager.LastSearch = current;
            }

            ConfigManager.SaveConfig();
            base.OnClosing(e);
        }

        private void RestoreWindowPosition() => LbpArchiveToolkit.Utils.WindowPositionManager.RestorePosition(this);

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadGenresAsync();
            await LoadSavedLevelsAsync();

            if (ConfigManager.LastSearch != null)
            {
                ApplySearchState(ConfigManager.LastSearch);
            }

            UpdateContributorFeatures();
            await CheckForUpdatesAsync();
        }

        private void UpdateContributorFeatures()
        {
            if (_dbService.HasContributorsTable)
            {
                cbiContributions.Visibility = Visibility.Visible;
                btnShowContributors.Visibility = Visibility.Visible;
            }
            else
            {
                cbiContributions.Visibility = Visibility.Collapsed;
                btnShowContributors.Visibility = Visibility.Collapsed;
                
                // Snap them back to level search if they restart the app into this unselectable state
                if (cmbSearchType.SelectedIndex == 2)
                {
                    cmbSearchType.SelectedIndex = 0;
                }
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            await LbpArchiveToolkit.Services.UpdateService.CheckForUpdatesAsync(this, SharedHttpClient);
        }

        private void SaveWindowPosition() => LbpArchiveToolkit.Utils.WindowPositionManager.SavePosition(this);

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
            catch (Exception ex)
            {
                LogManager.Log("MainWindow.LoadGenresAsync", ex);
            }
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

            if (cmbSearchType.SelectedIndex == 0)
            {
                BtnSearch_Click(btnSearch, null!);
            }
            else
            {
                cmbSearchType.SelectedIndex = 0;
            }
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

                await ExtractSelectedLevelsAsync(strictlyCreatorLevels);
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
                UpdateContributorFeatures();
                txtStatus.Text = "Config saved successfully.";
                _ = LoadGenresAsync();
            }
        }

        private void MenuBackupManager_Click(object sender, RoutedEventArgs e)
        {
            var backupWin = new BackupManagerWindow { Owner = this };
            backupWin.ShowDialog();
        }

        private void MenuLogViewer_Click(object sender, RoutedEventArgs e)
        {
            var logWin = new LogViewerWindow { Owner = this };
            logWin.ShowDialog();
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var aboutWin = new AboutWindow { Owner = this };
            aboutWin.ShowDialog();
        }

        private async void ShowToast(string message, UIElement placementTarget)
        {
            txtNotification.Text = message;

            // Anchor the popup to the element that was interacted with
            notificationToastPopup.PlacementTarget = placementTarget;
            notificationToastPopup.IsOpen = true;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200));
            notificationToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            _toastCts?.Cancel();
            _toastCts = new CancellationTokenSource();
            var token = _toastCts.Token;

            try
            {
                await Task.Delay(2000, token);
            }
            catch (TaskCanceledException)
            {
                return; // Another toast interrupted this one
            }

            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
            notificationToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            try
            {
                await Task.Delay(300, token);
                notificationToastPopup.IsOpen = false;
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                LogManager.Log("MainWindow.ShowToast", ex);
            }
        }

        private async void BtnShowContributors_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItem is LevelItem selectedLevel)
            {
                btnShowContributors.IsEnabled = false;
                try
                {
                    var contributors = await _dbService.GetContributorsAsync(selectedLevel.Id);
                    if (contributors.Count == 0)
                    {
                        CustomDialog.Show(this, "No contributors were found for this level.", "Contributors", false);
                    }
                    else
                    {
                        var dialog = new CustomDialog("", "Contributors", false) { Owner = this };
                        dialog.txtMessage.Inlines.Clear();

                        var noteRun = new Run("Note: May include creators who have changed their names or have no levels in the archive.\n\n")
                        {
                            FontSize = 12,
                            Foreground = (Brush)FindResource("FgSecondary")
                        };
                        dialog.txtMessage.Inlines.Add(noteRun);

                        foreach (var c in contributors)
                        {
                            var link = new Hyperlink(new Run("• " + c))
                            {
                                Foreground = (Brush)FindResource("LbpCyan"),
                                Cursor = Cursors.Hand,
                                TextDecorations = null
                            };

                            link.MouseEnter += (s, ev) => link.TextDecorations = TextDecorations.Underline;
                            link.MouseLeave += (s, ev) => link.TextDecorations = null;

                            string creatorName = c;
                            link.Click += (s, ev) =>
                            {
                                dialog.Close();
                                txtSearch.Text = creatorName;
                                chkExact.IsChecked = true;
                                
                                // Index 1 is the 'Creators' search type
                                if (cmbSearchType.SelectedIndex == 1)
                                {
                                    BtnSearch_Click(btnSearch, null!);
                                }
                                else
                                {
                                    // Changing the index automatically fires CmbSearchType_SelectionChanged, starting the search
                                    cmbSearchType.SelectedIndex = 1;
                                }
                            };
                            dialog.txtMessage.Inlines.Add(link);
                            dialog.txtMessage.Inlines.Add(new Run("\n"));
                        }

                        dialog.ShowDialog();
                    }
                }
                catch (Exception ex)
                {
                    CustomDialog.Show(this, $"Error fetching contributors: {ex.Message}", "Error", false);
                }
                finally
                {
                    btnShowContributors.IsEnabled = true;
                }
            }
        }

        private void BtnCopyHash_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItem is LevelItem selected && !string.IsNullOrEmpty(selected.Hash))
            {
                try
                {
                    Clipboard.SetText(selected.Hash);
                    ShowToast("Hash Copied!", btnCopyHash); // Float directly above the copy button
                }
                catch (Exception ex)
                {
                    LogManager.Log("MainWindow.BtnCopyHash_Click", ex);
                } // Silently catch OS clipboard lock exceptions
            }
        }

        private void CopyLevelNameContext_Click(object sender, RoutedEventArgs e)
        {
            if (dgResults.SelectedItem is LevelItem selected && !string.IsNullOrEmpty(selected.LevelName))
            {
                try
                {
                    Clipboard.SetText(selected.LevelName);

                    // If invoked via context menu, find the element that the context menu belongs to (DataGrid Cell OR Title TextBlock)
                    UIElement? target = null;
                    if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
                    {
                        target = contextMenu.PlacementTarget;
                    }

                    ShowToast("Level Name Copied!", target ?? this);
                }
                catch (Exception ex)
                {
                    LogManager.Log("MainWindow.CopyLevelNameContext_Click", ex);
                }
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
            bool isLevels = cmbSearchType.SelectedIndex == 0 || cmbSearchType.SelectedIndex == 2;
            bool isContrib = cmbSearchType.SelectedIndex == 2;

            panelLevelFilters?.Visibility = isLevels ? Visibility.Visible : Visibility.Collapsed;
            chkSearchDesc?.Visibility = (isLevels && !isContrib) ? Visibility.Visible : Visibility.Collapsed;
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

        private async Task PerformLevelSearchAsync(string keyword, bool exact, bool searchDesc, int gameFilter, string? genreFilter, string? limitFilter, AdvancedSearchCriteria criteria, CancellationToken token, string statusPrefix, bool searchContributions = false)
        {
            _resultsList = new ObservableCollection<LevelItem>();
            dgResults.ItemsSource = _resultsList;
            dgResults.Items.SortDescriptions.Clear();
            
            if (limitFilter == "All")
            {
                dgResults.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("Hearts", System.ComponentModel.ListSortDirection.Descending));
            }

            int count = 0;
            var sw = Stopwatch.StartNew();
            var progressReporter = new Progress<string>(status => txtStatus.Text = status);
            var savedLevelsSnapshot = _savedLevels.ToHashSet();
            var heartedLevelsSnapshot = HeartedLevelsManager.HeartedLevels.Select(x => x.Id).ToHashSet();

            await Task.Run(async () =>
            {
                var buffer = new List<LevelItem>();
                await foreach (var lvl in _dbService.SearchLevelsAsync(keyword, exact, searchDesc, gameFilter, genreFilter, limitFilter, savedLevelsSnapshot, heartedLevelsSnapshot, criteria, progressReporter, searchContributions, token).ConfigureAwait(false))
                {
                    buffer.Add(lvl);
                    count++;

                    if (sw.ElapsedMilliseconds > 500)
                    {
                        var chunk = buffer.ToList();
                        buffer.Clear();
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var item in chunk) _resultsList.Add(item);
                            txtStatus.Text = string.IsNullOrEmpty(keyword) ? $"{statusPrefix} {count} levels..." : $"{statusPrefix} {count} levels for '{keyword}'...";
                        }, System.Windows.Threading.DispatcherPriority.Background);
                        sw.Restart();
                    }
                }
                if (buffer.Count > 0)
                {
                    var chunk = buffer.ToList();
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        foreach (var item in chunk) _resultsList.Add(item);
                    });
                }
            });

            progressBar.IsIndeterminate = false;
            progressBar.Maximum = count;
            progressBar.Value = count;
            txtStatus.Text = string.IsNullOrEmpty(keyword) ? $"{statusPrefix} {count} levels." : $"{statusPrefix} {count} levels for '{keyword}'.";
        }

        private async Task PerformUserSearchAsync(string keyword, bool exact, string? limitFilter, CancellationToken token, string statusPrefix)
        {
            var results = await _dbService.SearchUsersAsync(keyword, exact, limitFilter, token);

            progressBar.IsIndeterminate = false;
            progressBar.Maximum = results.Count;
            progressBar.Value = results.Count;

            _userResultsList = results;
            dgUsers.ItemsSource = _userResultsList;
            txtStatus.Text = string.IsNullOrEmpty(keyword) ? $"{statusPrefix} {results.Count} creators." : $"{statusPrefix} {results.Count} creators matching '{keyword}'.";
        }

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            string keyword = txtSearch.Text.Trim();

            SetUIState(isSearching: true);
            txtStatus.Text = "Searching database...";
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;

            var current = _currentSearch;
            if (current != null)
            {
                // Save the currently selected item BEFORE we clear the DataGrid
                if (current.SearchTypeIndex == 0 || current.SearchTypeIndex == 2)
                    current.SelectedItem = dgResults.SelectedItem as LevelItem;
                else
                    current.SelectedUser = dgUsers.SelectedItem as UserItem;

                PushToHistory(_searchHistory, current);
            }

            // Clear previous results to avoid old icons flashing
            _resultsList = new ObservableCollection<LevelItem>();
            dgResults.ItemsSource = _resultsList;

            _userResultsList = new List<UserItem>();
            dgUsers.ItemsSource = _userResultsList;

            _forwardHistory.Clear();
            btnForward.IsEnabled = false;

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var searchToken = _searchCts.Token;

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
            if (searchType == 0 || searchType == 2)
            {
                bool searchContribs = searchType == 2;
                await PerformLevelSearchAsync(keyword, exact, searchDesc, gameFilter, genreFilter, limitFilter, _advancedCriteria, searchToken, "Found", searchContribs);

                if (dgResults.Items.Count > 0)
                {
                    dgResults.SelectedIndex = 0;
                    dgResults.ScrollIntoView(dgResults.Items[0]);
                }

                _currentSearch = new SearchState
                {
                    SearchText = keyword,
                    SearchTypeIndex = searchType,
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
                await PerformUserSearchAsync(keyword, exact, limitFilter, searchToken, "Found");

                if (_userResultsList.Any())
                {
                    dgUsers.SelectedIndex = 0;
                    dgUsers.ScrollIntoView(_userResultsList[0]);
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
            var current = _currentSearch;
            if (_searchHistory.Count > 0 && current != null)
            {
                if (current.SearchTypeIndex == 0 || current.SearchTypeIndex == 2)
                    current.SelectedItem = dgResults.SelectedItem as LevelItem;
                else
                    current.SelectedUser = dgUsers.SelectedItem as UserItem;

                PushToHistory(_forwardHistory, current);
                ApplySearchState(_searchHistory.Pop());
            }

            btnBack.IsEnabled = _searchHistory.Count > 0;
            btnForward.IsEnabled = _forwardHistory.Count > 0;
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            var current = _currentSearch;
            if (_forwardHistory.Count > 0 && current != null)
            {
                if (current.SearchTypeIndex == 0 || current.SearchTypeIndex == 2)
                    current.SelectedItem = dgResults.SelectedItem as LevelItem;
                else
                    current.SelectedUser = dgUsers.SelectedItem as UserItem;

                PushToHistory(_searchHistory, current);
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

            _resultsList = new ObservableCollection<LevelItem>();
            dgResults.ItemsSource = _resultsList;

            _userResultsList = new List<UserItem>();
            dgUsers.ItemsSource = _userResultsList;

            var savedLevelsSnapshot = _savedLevels.ToHashSet();
            var heartedLevelsSnapshot = HeartedLevelsManager.HeartedLevels.Select(x => x.Id).ToHashSet();
            string? limitFilter = (cmbLimit.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string? genreFilter = (cmbGenre.SelectedItem as ComboBoxItem)?.Content?.ToString();

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var searchToken = _searchCts.Token;

            try
        {
            if (state.SearchTypeIndex == 0 || state.SearchTypeIndex == 2)
            {
                bool searchContribs = state.SearchTypeIndex == 2;
                await PerformLevelSearchAsync(state.SearchText, state.Exact, state.SearchDesc, state.GameIndex, genreFilter, limitFilter, state.AdvancedCriteria, searchToken, "Restored", searchContribs);

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
                if (dgResults.SelectedItem == null && dgResults.Items.Count > 0)
                {
                    dgResults.SelectedIndex = 0;
                    dgResults.UpdateLayout();
                    dgResults.ScrollIntoView(dgResults.Items[0]);
                }
            }
            else
            {
                await PerformUserSearchAsync(state.SearchText, state.Exact, limitFilter, searchToken, "Restored");

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
                btnShowContributors.IsEnabled = true;

                btnHeartToggle.IsEnabled = true;
                RefreshCurrentSelectionHeartState();

                mmPickTails.Visibility = selectedLevel.IsMmPick ? Visibility.Visible : Visibility.Hidden;
                mmPickRosette.Visibility = selectedLevel.IsMmPick ? Visibility.Visible : Visibility.Hidden;
                mmPickRosetteInner.Visibility = selectedLevel.IsMmPick ? Visibility.Visible : Visibility.Hidden;
                iconEllipse.Stroke = selectedLevel.IsMmPick ? (Brush)FindResource("LbpPink") : (Brush)FindResource("LbpOrange");

                _currentIconRequestId = Interlocked.Increment(ref _iconRequestCounter);

                _iconCts?.Cancel();
                _iconCts = new CancellationTokenSource();

                await LoadIconAsync(selectedLevel.IconHash, _iconCts.Token);
            }
            else
            {
                btnExtract.IsEnabled = false;
                btnCopyHash.IsEnabled = false;
                btnShowContributors.IsEnabled = false;
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
                _currentIconRequestId = Interlocked.Increment(ref _iconRequestCounter);

                _iconCts?.Cancel();
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
            LbpArchiveToolkit.Utils.RichTextHelper.SetDescriptionRichText(txtDescription, text, name => 
            {
                txtSearch.Text = name;
                cmbSearchType.SelectedIndex = 1; 
            });
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
            long expectedRequestId = _currentIconRequestId;

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

            await ExtractSelectedLevelsAsync(selectedItems);
        }

        private async void BtnDownloadAllLevels_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem is UserItem selectedUser)
            {
                await InitiateBatchDownloadAsync(selectedUser);
            }
        }

        private async Task ExtractSelectedLevelsAsync(List<LevelItem> levels)
        {
            await LevelExtractionService.ExtractLevelsAsync(this, levels, lvl =>
            {
                _savedLevels.Add(lvl.Id);
                var existingItem = _resultsList.FirstOrDefault(x => x.Id == lvl.Id);
                if (existingItem != null) UpdateLevelSavedString(existingItem);
                UpdateLevelSavedString(lvl);
            });

            dgResults.Items.Refresh();
            txtStatus.Text = "Batch extraction finished.";
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
