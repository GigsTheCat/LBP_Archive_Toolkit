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
    /// <summary>
    /// The main application window. Handles user interactions, database searching, level selection, and triggering extractions.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region State & Dependencies

        private DatabaseService _dbService;
        private List<LevelItem> _resultsList = new();
        private readonly HashSet<string> _savedLevels = new();
        
        private readonly Stack<SearchState> _searchHistory = new();
        private readonly Stack<SearchState> _forwardHistory = new();
        private SearchState? _currentSearch = null;

        private long _currentIconRequestId = -1;

        private static readonly HttpClient _httpClient = new(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 10,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }) 
        { 
            Timeout = TimeSpan.FromMinutes(5) 
        };

        [GeneratedRegex(@"(\@[a-zA-Z0-9_-]+)")]
        private static partial Regex MentionRegex();

        /// <summary>
        /// Represents a snapshot of the UI state to enable forward/back browser-style navigation.
        /// </summary>
        public class SearchState
        {
            public string SearchText { get; set; } = "";
            public int GameIndex { get; set; }
            public int GenreIndex { get; set; }
            public int LimitIndex { get; set; }
            public bool Exact { get; set; }
            public bool SearchDesc { get; set; }
            public List<LevelItem> Results { get; set; } = new();
            public LevelItem? SelectedItem { get; set; }
        }

        #endregion

        #region Initialization & Lifecycle

        public MainWindow()
        {
            InitializeComponent();
                        
            ConfigManager.LoadConfig();
            _dbService = new DatabaseService(ConfigManager.DatabasePath);

            RestoreWindowPosition();
            this.SourceInitialized += Window_SourceInitialized;

            dgResults.ItemsSource = _resultsList;
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "LbpArchiveToolkit/1.0");

            if (ConfigManager.SavedLevels == null) ConfigManager.SavedLevels = new List<string>();

            LoadSavedLevels();
            _ = LoadGenresAsync(); 
            
            if (ConfigManager.LastSearch?.Results != null)
            {
                ApplySearchState(ConfigManager.LastSearch);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowPosition();

            if (_currentSearch != null)
            {
                _currentSearch.SelectedItem = dgResults.SelectedItem as LevelItem;
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
             await CheckForUpdatesAsync();
         }

         private async Task CheckForUpdatesAsync()
         {
             try
             {
                 string url = "https://api.github.com/repos/GigsTheCat/LBP_Archive_Toolkit/releases/latest";
                 var response = await _httpClient.GetStringAsync(url);
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
                             
                             bool update = CustomDialog.Show(this, $"A new version of LBP Archive Toolkit is available ({latestVersion}).\n\nWould you like to download it now?", "Update Available", isYesNo: true);
                             if (update)
                             {
                             Process.Start(new ProcessStartInfo("https://github.com/GigsTheCat/LBP_Archive_Toolkit/releases") { UseShellExecute = true });
                             }
                         }
                     }
                 }
             }
             catch { /* Silently fail if no internet or API limit reached */ }
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
                BtnSearch_Click(btnSearch, null!);
            }
        }

        #endregion

        #region Search & Navigation Logic

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            string keyword = txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(keyword)) return;

            SetUIState(isSearching: true);
            txtStatus.Text = "Searching database...";
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;

            if (_currentSearch != null)
            {
                _currentSearch.SelectedItem = dgResults.SelectedItem as LevelItem;
                _searchHistory.Push(_currentSearch);
            }

            _forwardHistory.Clear();
            btnForward.IsEnabled = false;

            bool exact = chkExact.IsChecked == true;
            bool searchDesc = chkSearchDesc.IsChecked == true;
            int gameFilter = cmbGame.SelectedIndex;
            int genreFilterIdx = cmbGenre.SelectedIndex;
            int limitFilterIdx = cmbLimit.SelectedIndex;
            string? genreFilter = (cmbGenre.SelectedItem as ComboBoxItem)?.Content?.ToString();
            string? limitFilter = (cmbLimit.SelectedItem as ComboBoxItem)?.Content?.ToString();

            try
            {
                var results = await _dbService.SearchLevelsAsync(keyword, exact, searchDesc, gameFilter, genreFilter, limitFilter, _savedLevels);

                progressBar.IsIndeterminate = false;
                progressBar.Maximum = results.Count;
                progressBar.Value = results.Count; // Update once, avoiding the loop penalty

                // Direct reference assignment, skipping O(N) array copy
                _resultsList = results;
                
                dgResults.ItemsSource = _resultsList;
                txtStatus.Text = $"Found {results.Count} results for '{keyword}'.";

                _currentSearch = new SearchState
                {
                    SearchText = keyword,
                    GameIndex = gameFilter,
                    GenreIndex = genreFilterIdx,
                    LimitIndex = limitFilterIdx,
                    Exact = exact,
                    SearchDesc = searchDesc,
                    Results = new List<LevelItem>(results) // Copy just for history immutability
                };

                btnBack.IsEnabled = _searchHistory.Count > 0;
            }
            catch (FileNotFoundException)
            {
                var missingDbDialog = new MissingDatabaseDialog { Owner = this };
                if (missingDbDialog.ShowDialog() == true)
                {
                    MenuSettings_Click(sender, e); 
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

        private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) BtnSearch_Click(sender, null!);
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (_searchHistory.Count > 0 && _currentSearch != null)
            {
                _currentSearch.SelectedItem = dgResults.SelectedItem as LevelItem;
                _forwardHistory.Push(_currentSearch);
                ApplySearchState(_searchHistory.Pop());
            }
            
            btnBack.IsEnabled = _searchHistory.Count > 0;
            btnForward.IsEnabled = _forwardHistory.Count > 0;
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (_forwardHistory.Count > 0 && _currentSearch != null)
            {
                _currentSearch.SelectedItem = dgResults.SelectedItem as LevelItem;
                _searchHistory.Push(_currentSearch);
                ApplySearchState(_forwardHistory.Pop());
            }

            btnBack.IsEnabled = _searchHistory.Count > 0;
            btnForward.IsEnabled = _forwardHistory.Count > 0;
        }

        private void ApplySearchState(SearchState state)
        {
            _currentSearch = state;

            txtSearch.Text = state.SearchText;
            cmbGame.SelectedIndex = state.GameIndex;
            cmbGenre.SelectedIndex = state.GenreIndex;
            cmbLimit.SelectedIndex = state.LimitIndex;
            chkExact.IsChecked = state.Exact;
            chkSearchDesc.IsChecked = state.SearchDesc;

            _resultsList = state.Results.ToList();
            dgResults.ItemsSource = _resultsList;

            if (state.SelectedItem != null)
            {
                var itemToSelect = _resultsList.FirstOrDefault(x => x.Id == state.SelectedItem.Id);
                if (itemToSelect != null)
                {
                    dgResults.SelectedItem = itemToSelect;
                    dgResults.ScrollIntoView(itemToSelect);
                }
            }
            else
            {
                dgResults.SelectedItem = null;
            }

            txtStatus.Text = $"Restored search for '{state.SearchText}'.";
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
            if (dgResults.SelectedItem is LevelItem selectedLevel)
            {
                txtLevelName.Text = selectedLevel.LevelName;
                txtCreator.Text = $"By: {selectedLevel.Creator}  |  Genre: {selectedLevel.Genre}  |  Plays: {selectedLevel.Plays}  |  ♥ {selectedLevel.Hearts}";
                
                SetDescriptionRichText(selectedLevel.Description);

                btnExtract.IsEnabled = true;
                btnCopyHash.IsEnabled = !string.IsNullOrEmpty(selectedLevel.Hash);

                _currentIconRequestId = selectedLevel.Id;
                await LoadIconAsync(selectedLevel.IconHash);
            }
            else
            {
                btnExtract.IsEnabled = false;
                btnCopyHash.IsEnabled = false;
                iconEllipse.Fill = (SolidColorBrush)FindResource("BgPrimary");
                txtIconStatus.Text = "Select a level\nto view details";
                
                txtDescription.Document.Blocks.Clear();
                txtLevelName.Text = "";
                txtCreator.Text = "";
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
                    BtnSearch_Click(btnSearch, null!);
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

        private async Task LoadIconAsync(string? hash)
        {
            iconEllipse.Fill = (SolidColorBrush)FindResource("BgPrimary");

            if (string.IsNullOrEmpty(hash) || hash.Length <= 8)
            {
                txtIconStatus.Text = "No Icon Available";
                return;
            }

            txtIconStatus.Text = "Loading Icon...";

            long expectedRequestId = _currentIconRequestId;

            if (dgResults.SelectedItem is LevelItem currentSelection)
            {
                if (currentSelection.Id != expectedRequestId) return;
            }
            else return;

            try
            {
                bool useLocalArchive = ConfigManager.DownloadServer.ToLower() == "local" && !string.IsNullOrWhiteSpace(ConfigManager.LocalArchivePath);

                if (useLocalArchive)
                {
                    try
                    {
                        byte[]? rawResource = await AssetDownloader.ExtractLocalArchiveToMemoryAsync(hash, ConfigManager.LocalArchivePath, CancellationToken.None);

                        if (rawResource != null)
                        {
                            byte[] pngBytes = await Task.Run(() =>
                            {
                                byte[] ddsData = TextureDecoder.DecodeLbpTexture(rawResource);
                                return TextureDecoder.ConvertDdsToPngCentered(ddsData);
                            });

                            if (_currentIconRequestId != expectedRequestId) return;

                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = new MemoryStream(pngBytes);
                            bmp.EndInit();
                            bmp.Freeze();

                            iconEllipse.Fill = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                            txtIconStatus.Text = "";
                            return; 
                        }
                    }
                    catch { /* Fallback to web request */ }
                }

                byte[] imageBytes = await _httpClient.GetByteArrayAsync($"https://zaprit.fish/icon/{hash}");

                if (_currentIconRequestId != expectedRequestId) return;

                var webBmp = new BitmapImage();
                webBmp.BeginInit();
                webBmp.CacheOption = BitmapCacheOption.OnLoad;
                webBmp.StreamSource = new MemoryStream(imageBytes);
                webBmp.EndInit();
                webBmp.Freeze(); 

                iconEllipse.Fill = new ImageBrush(webBmp) { Stretch = Stretch.UniformToFill };
                txtIconStatus.Text = "";
            }
            catch
            {
                if (_currentIconRequestId == expectedRequestId)
                {
                    txtIconStatus.Text = "Icon offline\nor missing.";
                }
            }
        }

        #endregion

        #region Extraction & Downloading Manager

        private async void BtnExtract_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = dgResults.SelectedItems.Cast<LevelItem>().ToList();
            if (!selectedItems.Any()) return;

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

                for (int i = 0; i < selectedItems.Count; i++)
                {
                    if (token.IsCancellationRequested) 
                    {
                        wasCancelled = true;
                        break;
                    }

                    var lvl = selectedItems[i];
                    string baseStatus = $"[{i + 1}/{selectedItems.Count}] Extracting: {lvl.LevelName}";
                    
                    progressWin.UpdateProgress(0, 1, baseStatus, "Initializing download...");

                    var progressIndicator = new Progress<(int processed, int total, string message)>(report =>
                    {
                        progressWin.UpdateProgress(report.processed, report.total, baseStatus, $"{report.message}\nProgress: {report.processed} / {report.total}");
                    });

                    try
                    {
                        var result = await AssetDownloader.RunExtractionProcessAsync(lvl, ConfigManager.DatabasePath, ConfigManager.BackupDirectory, _httpClient, token, progressIndicator);
                        
                        if (result.Success)
                        {
                            successCount++;
                            lvl.Saved = "✓";
                            _savedLevels.Add(lvl.Id.ToString());
                            
                            if (!ConfigManager.SavedLevels.Contains(lvl.Id.ToString()))
                            {
                                ConfigManager.SavedLevels.Add(lvl.Id.ToString());
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
                    ConfigManager.SaveConfig();
                }
            }
            finally
            {
                progressWin.Close();
                this.IsEnabled = true;
                AssetDownloader.CleanupLocalArchives();
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
                    if (Directory.Exists(fullPath)) Process.Start("explorer.exe", fullPath);
                }
            }
        }

        private void LoadSavedLevels()
        {
            foreach (var levelId in ConfigManager.SavedLevels)
            {
                _savedLevels.Add(levelId);
            }
            
            if (Directory.Exists(ConfigManager.BackupDirectory))
            {
                bool configNeedsUpdate = false;

                foreach (var file in Directory.EnumerateFiles(ConfigManager.BackupDirectory))
                {
                    ReadOnlySpan<char> name = Path.GetFileNameWithoutExtension(file.AsSpan());
                    int digitCount = 0;
                    
                    while (digitCount < name.Length && char.IsAsciiDigit(name[digitCount]))
                    {
                        digitCount++;
                    }

                    if (digitCount > 0)
                    {
                        string id = new string(name.Slice(0, digitCount));
                        _savedLevels.Add(id);
                        
                        if (!ConfigManager.SavedLevels.Contains(id))
                        {
                            ConfigManager.SavedLevels.Add(id);
                            configNeedsUpdate = true;
                        }
                    }
                }
                
                if (configNeedsUpdate) ConfigManager.SaveConfig();
            }
        }

        public void ClearSavedLevels()
        {
            _savedLevels.Clear();
            ConfigManager.SavedLevels.Clear();
            ConfigManager.SaveConfig();

            foreach (var item in _resultsList) item.Saved = string.Empty;
            
            if (_currentSearch?.Results != null)
            {
                foreach (var item in _currentSearch.Results) item.Saved = string.Empty;
            }

            foreach (var state in _searchHistory.Where(s => s.Results != null))
            {
                foreach (var item in state.Results) item.Saved = string.Empty;
            }

            foreach (var state in _forwardHistory.Where(s => s.Results != null))
            {
                foreach (var item in state.Results) item.Saved = string.Empty;
            }

            dgResults.Items.Refresh(); // Safely triggers visual layout rebuilds
        }

        #endregion

        #region Win32 Interop (Borderless Window Support)

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);

            if (ConfigManager.IsMaximized)
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0024) 
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;
            int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                GetMonitorInfo(monitor, ref monitorInfo);

                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;

                mmi.ptMaxPosition.X = Math.Abs(rcWorkArea.Left - rcMonitorArea.Left);
                mmi.ptMaxPosition.Y = Math.Abs(rcWorkArea.Top - rcMonitorArea.Top);
                mmi.ptMaxSize.X = Math.Abs(rcWorkArea.Right - rcWorkArea.Left);
                mmi.ptMaxSize.Y = Math.Abs(rcWorkArea.Bottom - rcWorkArea.Top);

                Marshal.StructureToPtr(mmi, lParam, true);
            }
        }

        [LibraryImport("user32.dll")]
        private static partial IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO 
        { 
            public POINT ptReserved; 
            public POINT ptMaxSize; 
            public POINT ptMaxPosition; 
            public POINT ptMinTrackSize; 
            public POINT ptMaxTrackSize; 
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        #endregion
    }
}