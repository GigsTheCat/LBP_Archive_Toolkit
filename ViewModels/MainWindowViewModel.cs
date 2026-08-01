using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace LbpArchiveToolkit.ViewModels
{
    public class BulkObservableCollection<T> : System.Collections.ObjectModel.ObservableCollection<T>
    {
        private bool _suppressNotification;

        protected override void OnCollectionChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotification)
                base.OnCollectionChanged(e);
        }

        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) return;
            
            _suppressNotification = true;
            foreach (var item in items) Add(item);
            _suppressNotification = false;
            
            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        }
    }

    public class TagItem : ViewModelBase
    {
        public string Text { get; set; } = "";
        public string? ToolTip { get; set; }
        public double TiltAngle { get; set; }
        public bool IsLbp1Tag { get; set; }
        public Visibility Visibility { get; set => SetProperty(ref field, value); } = Visibility.Visible;
    }

    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;
        private DatabaseService _dbService;

        public static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 10,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        }) { Timeout = TimeSpan.FromMinutes(5) };

        private CancellationTokenSource? _searchCts;
        private CancellationTokenSource? _iconCts;
        private long _currentIconRequestId = -1;

        private readonly HashSet<long> _savedLevels = new();
        
        private readonly Stack<SearchState> _searchHistory = new();
        private readonly Stack<SearchState> _forwardHistory = new();
        private SearchState? _currentSearch = null;

        public BulkObservableCollection<LevelItem> ResultsList { get; } = new();

        public List<UserItem> UserResultsList { get; set => SetProperty(ref field, value); } = new();

        public BulkObservableCollection<string> Genres { get; } = new() { "All Genres" };
        public BulkObservableCollection<TagItem> LevelTags { get; } = new();

        #region UI Properties

        public string SearchText { get; set => SetProperty(ref field, value); } = "";

        public int SearchTypeIndex
        {
            get;
            set
            {
                if (SetProperty(ref field, value))
                {
                    OnPropertyChanged(nameof(IsLevelSearch));
                    OnPropertyChanged(nameof(LevelViewVisibility));
                    OnPropertyChanged(nameof(UserViewVisibility));
                    OnPropertyChanged(nameof(SearchDescVisibility));
                    OnPropertyChanged(nameof(AdvancedButtonVisibility));
                    OnPropertyChanged(nameof(SurpriseButtonVisibility));
                    
                    if (!_isApplyingState && !string.IsNullOrWhiteSpace(SearchText))
                        SearchCommand.Execute(null);
                }
            }
        } = 0;

        public int GameIndex { get; set => SetProperty(ref field, value); } = 0;
        public string SelectedGenre { get; set => SetProperty(ref field, value); } = "All Genres";
        public int LimitIndex { get; set => SetProperty(ref field, value); } = 2;
        public bool ExactMatch { get; set => SetProperty(ref field, value); }
        public bool SearchDesc { get; set => SetProperty(ref field, value); }
        public string StatusText { get; set => SetProperty(ref field, value); } = "Ready. Enter a keyword or set filters to begin.";

        public bool IsSearching
        {
            get;
            set
            {
                SetProperty(ref field, value);
                OnPropertyChanged(nameof(SearchButtonVisibility));
                OnPropertyChanged(nameof(SurpriseButtonVisibility));
                OnPropertyChanged(nameof(CancelButtonVisibility));
            }
        }
        public Visibility SearchButtonVisibility => IsSearching ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SurpriseButtonVisibility => IsSearching ? Visibility.Collapsed : Visibility.Visible;
        public Visibility CancelButtonVisibility => IsSearching ? Visibility.Visible : Visibility.Collapsed;

        public Visibility IsProgressVisible { get; set => SetProperty(ref field, value); } = Visibility.Hidden;
        public bool IsProgressIndeterminate { get; set => SetProperty(ref field, value); }
        public int ProgressMaximum { get; set => SetProperty(ref field, value); } = 100;
        public int ProgressValue { get; set => SetProperty(ref field, value); } = 0;

        public bool IsLevelSearch => SearchTypeIndex == 0 || SearchTypeIndex == 2 || SearchTypeIndex == 3;
        public Visibility LevelViewVisibility => IsLevelSearch ? Visibility.Visible : Visibility.Collapsed;
        public Visibility UserViewVisibility => !IsLevelSearch ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SearchDescVisibility => (IsLevelSearch && SearchTypeIndex != 2 && SearchTypeIndex != 3) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AdvancedButtonVisibility => IsLevelSearch ? Visibility.Visible : Visibility.Collapsed;

        // DB Dependent Visibilities
        public Visibility HasContributorsVisibility => _dbService.HasContributorsTable ? Visibility.Visible : Visibility.Collapsed;
        public Visibility HasObjectContributorsVisibility => _dbService.HasObjectContributorsTable ? Visibility.Visible : Visibility.Collapsed;
        public bool HasCompletionData => _dbService.HasCompletionData;
        public bool HasCommunityLabels => _dbService.HasCommunityLabels;
        public bool HasExtendedSlotProperties => _dbService.HasExtendedSlotProperties;

        // Level Details Properties
        public LevelItem? SelectedLevel
        {
            get;
            set { if (SetProperty(ref field, value)) UpdateLevelDetails(); }
        }

        public Brush IconEllipseStroke { get; set => SetProperty(ref field, value); } = new SolidColorBrush(Color.FromRgb(255, 183, 3));
        public Brush IconEllipseFill { get; set => SetProperty(ref field, value); } = new SolidColorBrush(Color.FromRgb(25, 19, 43));
        public Brush OriginalIconFill { get; set => SetProperty(ref field, value); } = new SolidColorBrush(Color.FromRgb(25, 19, 43));
        public Visibility IconLockVisibility { get; set => SetProperty(ref field, value); } = Visibility.Hidden;
        public double IconScale { get; set => SetProperty(ref field, value); } = 1.0;
        public Visibility MmPickVisibility { get; set => SetProperty(ref field, value); } = Visibility.Hidden;
        public Visibility LevelHeartOverlayVisibility { get; set => SetProperty(ref field, value); } = Visibility.Hidden;
        public string IconStatusText { get; set => SetProperty(ref field, value); } = "Select a level\nto view details";
        public string HeartLevelButtonText { get; set => SetProperty(ref field, value); } = "♥ HEART LEVEL";
        public Visibility ToggleTagsButtonVisibility { get; set => SetProperty(ref field, value); } = Visibility.Collapsed;
        public string ToggleTagsButtonText { get; set => SetProperty(ref field, value); } = "SHOW TAGS";

        private bool _showingLbp1Tags = false;

        // User Details Properties
        public UserItem? SelectedUser
        {
            get;
            set { if (SetProperty(ref field, value)) UpdateUserDetails(); }
        }

        public Brush UserIconRectFill { get; set => SetProperty(ref field, value); } = new SolidColorBrush(Color.FromRgb(25, 19, 43));
        public Visibility UserHeartOverlayVisibility { get; set => SetProperty(ref field, value); } = Visibility.Hidden;
        public string UserIconStatusText { get; set => SetProperty(ref field, value); } = "Select a creator\nto view details";
        public string UserHeartButtonText { get; set => SetProperty(ref field, value); } = "♥ HEART CREATOR";

        public string UserStatsText => SelectedUser != null ? $"Hearts: {SelectedUser.HeartCount}  |  Total Levels: {SelectedUser.TotalLevels}" : "";
        public string UserSummaryText => SelectedUser != null ? 
            $"Published Level slots summary:\n• LBP1 Slots: {SelectedUser.Lbp1UsedSlots}\n• LBP2 Slots: {SelectedUser.Lbp2UsedSlots}\n• LBP3 Slots: {SelectedUser.Lbp3UsedSlots}\n\nClick the button below to view all levels published by {SelectedUser.NpHandle}." : "";

        public string LevelCreatorText => SelectedLevel != null ? $"By {SelectedLevel.Creator}" : "";

        public string LevelStatsText
        {
            get
            {
                if (SelectedLevel == null) return "";
                string clearsText = HasCompletionData ? $"  •  Clears: {SelectedLevel.Clears:N0}" : "";
                return $"Plays: {SelectedLevel.Plays:N0}  •  Yays: {SelectedLevel.Yays:N0}  •  ♥ {SelectedLevel.Hearts:N0}{clearsText}";
            }
        }

        #endregion

        private AdvancedSearchCriteria _advancedCriteria = new();
        private bool _isApplyingState = false;

        public MainWindowViewModel(IViewService viewService)
        {
            _viewService = viewService;
            _dbService = new DatabaseService(ConfigManager.DatabasePath);

            InitializeCommands();
        }

        public async Task LoadDataAsync(bool isStartup = false)
        {
            var dbGenres = await _dbService.GetGenresAsync();
            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = Genres.ToHashSet();
                var newGenres = dbGenres.OrderBy(x => x).Where(g => !existing.Contains(g)).ToList();
                if (newGenres.Count > 0)
                {
                    Genres.AddRange(newGenres);
                }
            });

            foreach (var levelId in SavedLevelsManager.SavedLevels)
                if (long.TryParse(levelId, out long parsedId)) _savedLevels.Add(parsedId);

            if (Directory.Exists(ConfigManager.BackupDirectory))
            {
                var discoveredIds = await Task.Run(() =>
                {
                    var ids = new List<long>();
                    foreach (var dir in Directory.EnumerateDirectories(ConfigManager.BackupDirectory))
                    {
                        string dirName = Path.GetFileName(dir);
                        if (dirName.Length >= 8 && long.TryParse(dirName.Substring(dirName.Length - 8), System.Globalization.NumberStyles.HexNumber, null, out long id))
                            ids.Add(id);
                    }
                    return ids;
                });

                bool needsUpdate = false;
                foreach (var id in discoveredIds)
                {
                    _savedLevels.Add(id);
                    if (!SavedLevelsManager.Contains(id.ToString()))
                    {
                        SavedLevelsManager.SavedLevels.Add(id.ToString());
                        needsUpdate = true;
                    }
                }
                if (needsUpdate) SavedLevelsManager.Save();
            }

            if (isStartup && ConfigManager.LastSearch != null)
                ApplySearchState(ConfigManager.LastSearch);
        }

        public void SaveState()
        {
            _searchCts?.Cancel();
            _iconCts?.Cancel();

            if (_currentSearch != null)
            {
                if (IsLevelSearch)
                    _currentSearch.SelectedItem = SelectedLevel;
                else
                    _currentSearch.SelectedUser = SelectedUser;

                ConfigManager.LastSearch = _currentSearch;
            }

            ConfigManager.SaveConfig();
        }

        public void RefreshDatabaseService()
        {
            _dbService = new DatabaseService(ConfigManager.DatabasePath);
            OnPropertyChanged(nameof(HasContributorsVisibility));
            OnPropertyChanged(nameof(HasObjectContributorsVisibility));
            OnPropertyChanged(nameof(HasCompletionData));
            OnPropertyChanged(nameof(LevelCreatorText));
            OnPropertyChanged(nameof(LevelStatsText));
            _ = LoadDataAsync();
        }

        public void RefreshHeartStates()
        {
            if (SelectedLevel != null)
            {
                LevelHeartOverlayVisibility = HeartedLevelsManager.IsHearted(SelectedLevel.Id) ? Visibility.Visible : Visibility.Hidden;
                HeartLevelButtonText = HeartedLevelsManager.IsHearted(SelectedLevel.Id) ? "♡ UNHEART LEVEL" : "♥ HEART LEVEL";
            }
            if (SelectedUser != null)
            {
                UserHeartOverlayVisibility = HeartedCreatorsManager.IsHearted(SelectedUser.NpHandle) ? Visibility.Visible : Visibility.Hidden;
                UserHeartButtonText = HeartedCreatorsManager.IsHearted(SelectedUser.NpHandle) ? "♡ UNHEART CREATOR" : "♥ HEART CREATOR";
            }
            foreach (var item in ResultsList) UpdateLevelSavedString(item);
        }

        public void ClearSavedLevels()
        {
            _savedLevels.Clear();
            SavedLevelsManager.Clear();
            foreach (var item in ResultsList) UpdateLevelSavedString(item);
        }
    }
}