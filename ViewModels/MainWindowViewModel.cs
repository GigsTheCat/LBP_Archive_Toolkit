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
        private Visibility _visibility = Visibility.Visible;
        public Visibility Visibility { get => _visibility; set => SetProperty(ref _visibility, value); }
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

        private List<UserItem> _userResultsList = new();
        public List<UserItem> UserResultsList { get => _userResultsList; set => SetProperty(ref _userResultsList, value); }

        public ObservableCollection<string> Genres { get; } = new() { "All Genres" };
        public ObservableCollection<TagItem> LevelTags { get; } = new();

        #region UI Properties

        private string _searchText = "";
        public string SearchText { get => _searchText; set => SetProperty(ref _searchText, value); }

        private int _searchTypeIndex = 0;
        public int SearchTypeIndex
        {
            get => _searchTypeIndex;
            set
            {
                if (SetProperty(ref _searchTypeIndex, value))
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
        }

        private int _gameIndex = 0;
        public int GameIndex { get => _gameIndex; set => SetProperty(ref _gameIndex, value); }

        private string _selectedGenre = "All Genres";
        public string SelectedGenre { get => _selectedGenre; set => SetProperty(ref _selectedGenre, value); }

        private int _limitIndex = 2;
        public int LimitIndex { get => _limitIndex; set => SetProperty(ref _limitIndex, value); }

        private bool _exactMatch;
        public bool ExactMatch { get => _exactMatch; set => SetProperty(ref _exactMatch, value); }

        private bool _searchDesc;
        public bool SearchDesc { get => _searchDesc; set => SetProperty(ref _searchDesc, value); }

        private string _statusText = "Ready. Enter a keyword or set filters to begin.";
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        private bool _isSearching;
        public bool IsSearching
        {
            get => _isSearching;
            set
            {
                SetProperty(ref _isSearching, value);
                OnPropertyChanged(nameof(SearchButtonVisibility));
                OnPropertyChanged(nameof(SurpriseButtonVisibility));
                OnPropertyChanged(nameof(CancelButtonVisibility));
            }
        }
        public Visibility SearchButtonVisibility => IsSearching ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SurpriseButtonVisibility => IsSearching ? Visibility.Collapsed : Visibility.Visible;
        public Visibility CancelButtonVisibility => IsSearching ? Visibility.Visible : Visibility.Collapsed;

        private Visibility _isProgressVisible = Visibility.Hidden;
        public Visibility IsProgressVisible { get => _isProgressVisible; set => SetProperty(ref _isProgressVisible, value); }

        private bool _isProgressIndeterminate;
        public bool IsProgressIndeterminate { get => _isProgressIndeterminate; set => SetProperty(ref _isProgressIndeterminate, value); }

        private int _progressMaximum = 100;
        public int ProgressMaximum { get => _progressMaximum; set => SetProperty(ref _progressMaximum, value); }

        private int _progressValue = 0;
        public int ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

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
        private LevelItem? _selectedLevel;
        public LevelItem? SelectedLevel
        {
            get => _selectedLevel;
            set { if (SetProperty(ref _selectedLevel, value)) UpdateLevelDetails(); }
        }

        private Brush _iconEllipseStroke = new SolidColorBrush(Color.FromRgb(255, 183, 3));
        public Brush IconEllipseStroke { get => _iconEllipseStroke; set => SetProperty(ref _iconEllipseStroke, value); }

        private Brush _iconEllipseFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
        public Brush IconEllipseFill { get => _iconEllipseFill; set => SetProperty(ref _iconEllipseFill, value); }

        private Brush _originalIconFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
        public Brush OriginalIconFill { get => _originalIconFill; set => SetProperty(ref _originalIconFill, value); }

        private Visibility _iconLockVisibility = Visibility.Hidden;
        public Visibility IconLockVisibility { get => _iconLockVisibility; set => SetProperty(ref _iconLockVisibility, value); }

        private double _iconScale = 1.0;
        public double IconScale { get => _iconScale; set => SetProperty(ref _iconScale, value); }

        private Visibility _mmPickVisibility = Visibility.Hidden;
        public Visibility MmPickVisibility { get => _mmPickVisibility; set => SetProperty(ref _mmPickVisibility, value); }

        private Visibility _levelHeartOverlayVisibility = Visibility.Hidden;
        public Visibility LevelHeartOverlayVisibility { get => _levelHeartOverlayVisibility; set => SetProperty(ref _levelHeartOverlayVisibility, value); }

        private string _iconStatusText = "Select a level\nto view details";
        public string IconStatusText { get => _iconStatusText; set => SetProperty(ref _iconStatusText, value); }

        private string _heartLevelButtonText = "♥ HEART LEVEL";
        public string HeartLevelButtonText { get => _heartLevelButtonText; set => SetProperty(ref _heartLevelButtonText, value); }

        private Visibility _toggleTagsButtonVisibility = Visibility.Collapsed;
        public Visibility ToggleTagsButtonVisibility { get => _toggleTagsButtonVisibility; set => SetProperty(ref _toggleTagsButtonVisibility, value); }

        private string _toggleTagsButtonText = "SHOW TAGS";
        public string ToggleTagsButtonText { get => _toggleTagsButtonText; set => SetProperty(ref _toggleTagsButtonText, value); }

        private bool _showingLbp1Tags = false;

        // User Details Properties
        private UserItem? _selectedUser;
        public UserItem? SelectedUser
        {
            get => _selectedUser;
            set { if (SetProperty(ref _selectedUser, value)) UpdateUserDetails(); }
        }

        private Brush _userIconRectFill = new SolidColorBrush(Color.FromRgb(25, 19, 43));
        public Brush UserIconRectFill { get => _userIconRectFill; set => SetProperty(ref _userIconRectFill, value); }

        private Visibility _userHeartOverlayVisibility = Visibility.Hidden;
        public Visibility UserHeartOverlayVisibility { get => _userHeartOverlayVisibility; set => SetProperty(ref _userHeartOverlayVisibility, value); }

        private string _userIconStatusText = "Select a creator\nto view details";
        public string UserIconStatusText { get => _userIconStatusText; set => SetProperty(ref _userIconStatusText, value); }

        private string _userHeartButtonText = "♥ HEART CREATOR";
        public string UserHeartButtonText { get => _userHeartButtonText; set => SetProperty(ref _userHeartButtonText, value); }

        public string UserStatsText => SelectedUser != null ? $"Hearts: {SelectedUser.HeartCount}  |  Total Levels: {SelectedUser.TotalLevels}" : "";
        public string UserSummaryText => SelectedUser != null ? 
            $"Published Level slots summary:\n• LBP1 Slots: {SelectedUser.Lbp1UsedSlots}\n• LBP2 Slots: {SelectedUser.Lbp2UsedSlots}\n• LBP3 Slots: {SelectedUser.Lbp3UsedSlots}\n\nClick the button below to view all levels published by {SelectedUser.NpHandle}." : "";

        public string LevelCreatorAndStatsText
        {
            get
            {
                if (SelectedLevel == null) return "";
                string clearsText = HasCompletionData ? $"  |  Clears: {SelectedLevel.Clears}" : "";
                return $"By: {SelectedLevel.Creator}  |  Genre: {SelectedLevel.Genre}  |  Plays: {SelectedLevel.Plays}{clearsText}  |  ♥ {SelectedLevel.Hearts}";
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
                foreach (var g in dbGenres.OrderBy(x => x))
                {
                    if (!existing.Contains(g)) Genres.Add(g);
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
            OnPropertyChanged(nameof(LevelCreatorAndStatsText));
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