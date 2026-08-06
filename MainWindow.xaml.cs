using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LbpArchiveToolkit
{
    public partial class MainWindow : Window, IViewService
    {
        private MainWindowViewModel _viewModel;
        private CancellationTokenSource? _toastCts;

        // --- Backwards Compatibility Passthroughs for Child Windows ---
        public static readonly HttpClient SharedHttpClient = MainWindowViewModel.SharedHttpClient;
        
        public bool HasContributorsTable => _viewModel.HasContributorsVisibility == Visibility.Visible;
        public bool HasObjectContributorsTable => _viewModel.HasObjectContributorsVisibility == Visibility.Visible;
        
        public void InitiateCreatorSearch(string npHandle) => _viewModel.InitiateCreatorSearch(npHandle);
        public void InitiateContributionsSearch(string npHandle) => _viewModel.InitiateContributionsSearch(npHandle);
        public void InitiateObjectsSearch(string npHandle) => _viewModel.InitiateObjectsSearch(npHandle);
        public Task InitiateBatchDownloadAsync(UserItem user) => _viewModel.BatchDownloadAsync(user);
        public void ClearSavedLevels() => _viewModel.ClearSavedLevels();
        // --------------------------------------------------------------

        public MainWindow()
        {
            InitializeComponent();

            ConfigManager.LoadConfig();
            SavedLevelsManager.Load(ConfigManager.LegacySavedLevels);
            HeartedLevelsManager.Load();
            HeartedCreatorsManager.Load();
            PlaylistsManager.Load();

            LbpArchiveToolkit.Themes.ThemeManager.ApplyTheme(ConfigManager.Theme);

            _viewModel = new MainWindowViewModel(this);
            DataContext = _viewModel;
            _viewModel.PropertyChanged += Vm_PropertyChanged;

            LbpArchiveToolkit.Utils.WindowPositionManager.RestorePosition(this);
            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);

            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(ContextMenu_Opened));

            this.SourceInitialized += (s, e) =>
            {
                if (ConfigManager.IsMaximized) this.WindowState = WindowState.Maximized;
            };

            Loaded += MainWindow_Loaded;
        }

        private void TxtDescription_Copy_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _viewModel.SelectedLevel != null && !string.IsNullOrEmpty(_viewModel.SelectedLevel.Description);
            e.Handled = true;
        }

        private void TxtDescription_Copy_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                string textToCopy = string.Empty;
                
                if (!txtDescription.Selection.IsEmpty)
                {
                    textToCopy = txtDescription.Selection.Text;
                }
                else if (_viewModel.SelectedLevel != null)
                {
                    textToCopy = _viewModel.SelectedLevel.Description ?? string.Empty;
                }

                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy);
                    ShowToast("Copied!", "Mouse");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log("TxtDescription_Copy", ex);
            }
            e.Handled = true;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.LoadDataAsync(true);
            
            _ = Services.UpdateService.CheckForUpdatesAsync(this, SharedHttpClient);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            LbpArchiveToolkit.Utils.WindowPositionManager.SavePosition(this);
            _viewModel.SaveState();
            base.OnClosing(e);
        }

        private FrameworkElement? _lastContextElement;
        private Point _lastRightClickPoint;

        private async void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedLevel) || e.PropertyName == nameof(MainWindowViewModel.SelectedLevelDescription))
            {
                if (_viewModel.SelectedLevel != null)
                {
                    var selected = _viewModel.SelectedLevel;

                    if (selected.Description == null)
                    {
                        txtDescription.Document.Blocks.Clear();
                        txtDescription.Document.Blocks.Add(new Paragraph(new Run("Loading description...")));
                    }
                    else
                    {
                        LbpArchiveToolkit.Utils.RichTextHelper.SetDescriptionRichText(txtDescription, selected.Description, name =>
                        {
                            _viewModel.InitiateUserSearch(name);
                        });
                    }

                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_viewModel.SelectedLevel != null)
                        {
                            dgResults.ScrollIntoView(_viewModel.SelectedLevel);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    txtDescription.Document.Blocks.Clear();
                }
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.SelectedUser))
            {
                if (_viewModel.SelectedUser != null)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_viewModel.SelectedUser != null)
                        {
                            dgUsers.ScrollIntoView(_viewModel.SelectedUser);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.HasCompletionData))
            {
                colClears.Visibility = _viewModel.HasCompletionData ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var menu = e.Source as ContextMenu ?? sender as ContextMenu;
            if (menu != null)
            {
                if (menu.PlacementTarget is FrameworkElement feContext)
                {
                    _lastContextElement = feContext;
                }

                string? creatorName = null;
                if (menu.PlacementTarget is FrameworkElement fe)
                {
                    if (fe.DataContext is LevelItem level)
                        creatorName = level.Creator;
                    else if (fe.DataContext is MainWindowViewModel vm && vm.SelectedLevel != null)
                        creatorName = vm.SelectedLevel.Creator;
                }

                if (!string.IsNullOrEmpty(creatorName))
                {
                    bool isHearted = HeartedCreatorsManager.IsHearted(creatorName);
                    foreach (var item in menu.Items)
                    {
                        if (item is MenuItem menuItem)
                        {
                            if (menuItem.Name == "MenuHeartCreator")
                                menuItem.Header = isHearted ? "Unheart Creator" : "Heart Creator";
                            else if (menuItem.Name == "MenuSearchContributions")
                                menuItem.Visibility = _viewModel.HasContributorsVisibility;
                            else if (menuItem.Name == "MenuSearchObjects")
                                menuItem.Visibility = _viewModel.HasObjectContributorsVisibility;
                        }
                    }
                }
            }

            _lastRightClickPoint = Mouse.GetPosition(this);
        }

        #region Title Bar Handlers
        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();
        #endregion

        #region IViewService Implementation
        public Window GetMainWindow() => this;

        public bool ShowMissingDatabaseDialog()
        {
            var dlg = new MissingDatabaseDialog { Owner = this };
            return dlg.ShowDialog() == true;
        }

        public void ShowSettingsDialog()
        {
            new SettingsWindow { Owner = this }.ShowDialog();
        }

        public (AdvancedSearchCriteria Criteria, bool ShouldSearch)? ShowAdvancedSearchDialog(AdvancedSearchCriteria current, bool hasCommunityLabels, bool hasExtendedSlotProperties)
        {
            var dlg = new AdvancedSearchWindow(current, hasCommunityLabels, hasExtendedSlotProperties, this) { Owner = this };
            return dlg.ShowDialog() == true ? (dlg.Criteria, dlg.ShouldSearch) : null;
        }

        public void OpenBackupManager() => new BackupManagerWindow { Owner = this }.ShowDialog();
        public void OpenHeartedLevels() => new HeartedLevelsWindow { Owner = this }.ShowDialog();
        public void OpenHeartedCreators() => new HeartedCreatorsWindow { Owner = this }.ShowDialog();
        public void OpenPlaylists() => new PlaylistsWindow { Owner = this }.ShowDialog();
        public void ShowAddToPlaylistDialog(LevelItem level) => new AddToPlaylistDialog(level) { Owner = this }.ShowDialog();
        public void OpenDownloads() => new DownloadsWindow { Owner = this }.ShowDialog();
        public void OpenLogViewer() => new LogViewerWindow { Owner = this }.ShowDialog();
        public void OpenAbout() => new AboutWindow { Owner = this }.ShowDialog();

        public bool Confirm(string message, string title) => CustomDialog.Show(this, message, title, true);
        public void Alert(string message, string title) => CustomDialog.Show(this, message, title, false);

        public async void ShowToast(string message, string targetElementName)
        {
            txtNotification.Text = message;

            if (targetElementName == "Mouse")
            {
                notificationToastPopup.PlacementTarget = null;
                notificationToastPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                notificationToastPopup.HorizontalOffset = 15;
                notificationToastPopup.VerticalOffset = 15;
            }
            else if (targetElementName == "ContextElement")
            {
                if (_lastContextElement != null && _lastContextElement.IsVisible)
                {
                    notificationToastPopup.PlacementTarget = _lastContextElement;
                    notificationToastPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                    notificationToastPopup.HorizontalOffset = 0;
                    notificationToastPopup.VerticalOffset = -8;
                }
                else
                {
                    notificationToastPopup.PlacementTarget = this;
                    notificationToastPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
                    notificationToastPopup.HorizontalOffset = _lastRightClickPoint.X + 15;
                    notificationToastPopup.VerticalOffset = _lastRightClickPoint.Y + 15;
                }
            }
            else
            {
                var target = this.FindName(targetElementName) as UIElement;
                if (target != null && !target.IsVisible && targetElementName == "btnExtract")
                {
                    target = this.FindName("btnBatchDownload") as UIElement;
                }

                notificationToastPopup.PlacementTarget = target ?? this;
                notificationToastPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                notificationToastPopup.HorizontalOffset = 0;
                notificationToastPopup.VerticalOffset = -8;
            }

            notificationToastPopup.IsOpen = true;

            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200));
            notificationToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            _toastCts?.Cancel();
            _toastCts = new CancellationTokenSource();
            var token = _toastCts.Token;

            try { await Task.Delay(2000, token); } catch (TaskCanceledException) { return; }

            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(300));
            notificationToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            try
            {
                await Task.Delay(300, token);
                notificationToastPopup.IsOpen = false;
            }
            catch (TaskCanceledException) { }
        }

        public void ShowContributorsDialog(List<string> contributors, List<string> objectContributors, List<(long id, string name)> objectOrigins, string levelCreator, Action<string> onCreatorClicked, Action<long> onLevelClicked)
        {
            if (contributors.Count == 0 && objectContributors.Count == 0 && objectOrigins.Count == 0)
            {
                Alert("No contributors or object origins were found for this level.", "Contributors");
                return;
            }

            var dialog = new CustomDialog("", "Contributors", false) { Owner = this };
            
            // Expand the generic dialog for the contributors view
            dialog.Width = 650;
            dialog.scrollMessage.MaxHeight = 600;

            dialog.txtMessage.Inlines.Clear();
            dialog.txtMessage.Inlines.Add(new Run("Note: May include creators who have changed their names or have no levels in the archive.\n\n") { FontSize = 12, Foreground = (Brush)FindResource("FgSecondary") });

            void PopulateLinks(string header, List<string> names)
            {
                dialog.txtMessage.Inlines.Add(new Run(header + "\n") { FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("LbpOrange") });
                foreach (var c in names)
                {
                    var link = new Hyperlink(new Run("• " + c)) { Foreground = (Brush)FindResource("LbpCyan"), Cursor = Cursors.Hand, TextDecorations = null };
                    link.MouseEnter += (s, ev) => link.TextDecorations = TextDecorations.Underline;
                    link.MouseLeave += (s, ev) => link.TextDecorations = null;
                    string name = c;
                    link.Click += (s, ev) => { dialog.Close(); onCreatorClicked(name); };
                    LbpArchiveToolkit.Utils.CreatorPreviewBehavior.SetCreatorName(link, name);
                    dialog.txtMessage.Inlines.Add(link);
                    dialog.txtMessage.Inlines.Add(new Run("\n"));
                }
            }

            void PopulateLevelLinks(string header, List<(long id, string name)> levels)
            {
                dialog.txtMessage.Inlines.Add(new Run(header + "\n") { FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("LbpOrange") });
                foreach (var l in levels)
                {
                    var link = new Hyperlink(new Run("• " + l.name)) { Foreground = (Brush)FindResource("LbpEmerald"), Cursor = Cursors.Hand, TextDecorations = null };
                    link.MouseEnter += (s, ev) => link.TextDecorations = TextDecorations.Underline;
                    link.MouseLeave += (s, ev) => link.TextDecorations = null;
                    long id = l.id;
                    link.Click += (s, ev) => { dialog.Close(); onLevelClicked(id); };
                    dialog.txtMessage.Inlines.Add(link);
                    dialog.txtMessage.Inlines.Add(new Run("\n"));
                }
            }

            if (contributors.Count > 0)
            {
                PopulateLinks("Level Contributors:", contributors);
                if (objectContributors.Count > 0 || objectOrigins.Count > 0) dialog.txtMessage.Inlines.Add(new Run("\n"));
            }
            if (objectContributors.Count > 0)
            {
                PopulateLinks("Object Contributors:", objectContributors);
                if (objectOrigins.Count > 0) dialog.txtMessage.Inlines.Add(new Run("\n"));
            }
            if (objectOrigins.Count > 0)
            {
                PopulateLevelLinks("Uses objects from these levels:", objectOrigins);
            }

            dialog.ShowDialog();
        }

        public void ShowObjectUsagesDialog(List<(long id, string name)> levels, string originLevelName, Action<long> onLevelClicked)
        {
            var dialog = new CustomDialog("", "Object Usages", false) { Owner = this };
            
            // Expand the generic dialog for the usages view
            dialog.Width = 650;
            dialog.scrollMessage.MaxHeight = 600;

            dialog.txtMessage.Inlines.Clear();
            dialog.txtMessage.Inlines.Add(new Run($"This is the possible origin for objects used in the following levels:\n\n") { FontSize = 14, Foreground = (Brush)FindResource("FgPrimary") });

            foreach (var l in levels)
            {
                var link = new Hyperlink(new Run("• " + l.name)) { Foreground = (Brush)FindResource("LbpEmerald"), Cursor = Cursors.Hand, TextDecorations = null };
                link.MouseEnter += (s, ev) => link.TextDecorations = TextDecorations.Underline;
                link.MouseLeave += (s, ev) => link.TextDecorations = null;
                long id = l.id;
                link.Click += (s, ev) => { dialog.Close(); onLevelClicked(id); };
                dialog.txtMessage.Inlines.Add(link);
                dialog.txtMessage.Inlines.Add(new Run("\n"));
            }

            dialog.ShowDialog();
        }
        #endregion
    }
}