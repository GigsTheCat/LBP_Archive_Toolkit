using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Themes;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class SettingsWindowViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;
        private bool _isInitialized;
        private bool _promptedForDb;

        public Action<bool>? RequestClose { get; set; }

        // --- Properties ---

        public string DatabasePath
        {
            get;
            set
            {
                if (SetProperty(ref field, value))
                {
                    UpdateRamUsageText();
                    if (_isInitialized) CheckDbFeatures(value);
                }
            }
        } = "";

        public string BackupDirectory { get; set => SetProperty(ref field, value); } = "";
        public string LocalArchivePath { get; set => SetProperty(ref field, value); } = "";
        public string DownloadServer { get; set => SetProperty(ref field, value); } = "bonsai";
        public string MaxParallelDownloads { get; set => SetProperty(ref field, value); } = "10";

        public ObservableCollection<KeyValuePair<string, string>> AvailableThemes { get; } = new();
        public KeyValuePair<string, string> SelectedTheme
        {
            get;
            set
            {
                if (SetProperty(ref field, value) && _isInitialized)
                {
                    ThemeManager.ApplyTheme(value.Key);
                }
            }
        }

        public ObservableCollection<string> AvailableRegions { get; } = new() { "US (NTSC-U)", "EU (PAL)", "JP (NTSC-J)" };
        
        public string SelectedRegion { get; set => SetProperty(ref field, value); } = "EU (PAL)";
        public bool ForceLbp3Backups { get; set => SetProperty(ref field, value); }
        public bool Lbp2BetaToRetail { get; set => SetProperty(ref field, value); }
        public bool UseMemoryMappedIO { get; set => SetProperty(ref field, value); }
        public bool LoadDbIntoRam { get; set => SetProperty(ref field, value); }
        public bool ShowExtractionSuccessPrompt { get; set => SetProperty(ref field, value); }
        public string RamUsageText { get; set => SetProperty(ref field, value); } = "Load entire DB into RAM (Extreme speed, requires free RAM based on DB size)";

        // --- Commands ---

        public ICommand BrowseDbCommand { get; }
        public ICommand BrowseBackupCommand { get; }
        public ICommand BrowseLocalArchiveCommand { get; }
        public ICommand ForgetLevelsCommand { get; }
        public ICommand ResetDefaultsCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public SettingsWindowViewModel(IViewService viewService)
        {
            _viewService = viewService;

            BrowseDbCommand = new RelayCommand(_ => ExecuteBrowseDb());
            BrowseBackupCommand = new RelayCommand(_ => ExecuteBrowseBackup());
            BrowseLocalArchiveCommand = new RelayCommand(_ => ExecuteBrowseLocalArchive());
            ForgetLevelsCommand = new RelayCommand(_ => ExecuteForgetLevels());
            ResetDefaultsCommand = new RelayCommand(_ => ExecuteResetDefaults());
            SaveCommand = new RelayCommand(_ => ExecuteSave());
            CancelCommand = new RelayCommand(_ => ExecuteCancel());

            LoadConfigToUI();
            _isInitialized = true;
        }

        private void LoadConfigToUI()
        {
            DatabasePath = ConfigManager.DatabasePath;
            BackupDirectory = ConfigManager.BackupDirectory;
            LocalArchivePath = ConfigManager.LocalArchivePath;
            DownloadServer = ConfigManager.DownloadServer;
            MaxParallelDownloads = ConfigManager.MaxParallelDownloads.ToString();
            ForceLbp3Backups = ConfigManager.ForceLbp3Backups;
            Lbp2BetaToRetail = ConfigManager.Lbp2BetaToRetail;
            UseMemoryMappedIO = ConfigManager.UseMemoryMappedIO;
            LoadDbIntoRam = ConfigManager.LoadDbIntoRam;
            ShowExtractionSuccessPrompt = ConfigManager.ShowExtractionSuccessPrompt;

            AvailableThemes.Clear();
            foreach (var theme in ThemeManager.AvailableThemes)
            {
                AvailableThemes.Add(theme);
                if (theme.Key == ConfigManager.Theme)
                {
                    SelectedTheme = theme;
                }
            }

            var matchingRegion = AvailableRegions.FirstOrDefault(r => r.StartsWith(ConfigManager.GameRegion));
            if (matchingRegion != null)
            {
                SelectedRegion = matchingRegion;
            }

            UpdateRamUsageText();
        }

        private void UpdateRamUsageText()
        {
            if (File.Exists(DatabasePath))
            {
                try
                {
                    var fileInfo = new FileInfo(DatabasePath);
                    double gbSize = fileInfo.Length / (1024.0 * 1024.0 * 1024.0);
                    RamUsageText = $"Load entire DB into RAM (Extreme speed, requires ~{gbSize:F1} GB free RAM)";
                }
                catch
                {
                    RamUsageText = "Load entire DB into RAM (Extreme speed, requires free RAM based on DB size)";
                }
            }
            else
            {
                RamUsageText = "Load entire DB into RAM (Extreme speed, requires free RAM based on DB size)";
            }
        }

        private void CheckDbFeatures(string dbPath)
        {
            if (_promptedForDb || !File.Exists(dbPath)) return;

            try
            {
                var connStringBuilder = new SqliteConnectionStringBuilder { DataSource = dbPath };
                using var conn = new SqliteConnection(connStringBuilder.ConnectionString);
                conn.Open();

                using var cmdFts = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='slot_fts'", conn);
                bool hasFts = Convert.ToInt32(cmdFts.ExecuteScalar()) > 0;

                using var cmdContrib = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='level_contributors'", conn);
                bool hasContrib = Convert.ToInt32(cmdContrib.ExecuteScalar()) > 0;

                using var cmdCompletion = new SqliteCommand("SELECT count(*) FROM pragma_table_info('slot') WHERE name='completionCount' OR name='completions'", conn);
                bool hasCompletion = Convert.ToInt32(cmdCompletion.ExecuteScalar()) > 0;

                using var cmdObjOrigins = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='object_origins'", conn);
                bool hasObjOrigins = Convert.ToInt32(cmdObjOrigins.ExecuteScalar()) > 0;

                if (!hasFts || !hasContrib || !hasCompletion || !hasObjOrigins)
                {
                    _promptedForDb = true;

                    var missing = new List<string>();
                    if (!hasFts) missing.Add("• FTS5 Hardware Acceleration (Slower searches)");
                    if (!hasContrib) missing.Add("• Contributor Data (Contributor features disabled)");
                    if (!hasCompletion) missing.Add("• Level Completion Statistics (Completion counts won't be shown)");
                    if (!hasObjOrigins) missing.Add("• Object Origins (Object usage lookups disabled)");

                    string msg = $"The selected database is an older version and lacks the following features:\n\n{string.Join("\n", missing)}\n\nWould you like to download the newer version from archive.org to enable these features?";

                    bool download = _viewService.Confirm(msg, "Outdated Database");
                    if (download)
                    {
                        Process.Start(new ProcessStartInfo("https://archive.org/download/ultimatefastdry") { UseShellExecute = true });
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log("SettingsWindowViewModel.CheckDbFeatures", ex);
            }
        }

        private void ExecuteBrowseDb()
        {
            var dialog = new OpenFileDialog { Filter = "Database Files (*.db)|*.db|All Files (*.*)|*.*", Title = "Select dry.db file" };
            if (dialog.ShowDialog() == true) DatabasePath = dialog.FileName;
        }

        private void ExecuteBrowseBackup()
        {
            var dialog = new OpenFolderDialog { Title = "Select Backup Directory" };
            if (dialog.ShowDialog() == true) BackupDirectory = dialog.FolderName;
        }

        private void ExecuteBrowseLocalArchive()
        {
            var dialog = new OpenFolderDialog { Title = "Select Local Archive Directory" };
            if (dialog.ShowDialog() == true) LocalArchivePath = dialog.FolderName;
        }

        private void ExecuteForgetLevels()
        {
            bool result = _viewService.Confirm("Are you sure you want to forget all saved levels?\nThis takes effect immediately and cannot be undone.", "Forget Saved Levels");
            if (result)
            {
                if (_viewService.GetMainWindow() is MainWindow main)
                    main.ClearSavedLevels();
                else
                    SavedLevelsManager.Clear();

                _viewService.Alert("All saved levels have been cleared.", "Success");
            }
        }

        private void ExecuteResetDefaults()
        {
            bool result = _viewService.Confirm("Reset all configuration fields to their default values?\n(You still need to click 'Save Settings' to apply this change.)", "Reset to Default");
            if (result)
            {
                DatabasePath = "dry.db";
                BackupDirectory = "backups";
                LocalArchivePath = "";
                DownloadServer = "bonsai";
                MaxParallelDownloads = "10";
                ForceLbp3Backups = false;
                Lbp2BetaToRetail = true;
                UseMemoryMappedIO = false;
                LoadDbIntoRam = false;
                ShowExtractionSuccessPrompt = true;
                SelectedRegion = AvailableRegions.FirstOrDefault(r => r.StartsWith("EU")) ?? "EU (PAL)";
            }
        }

        private async void ExecuteSave()
        {
            if (!int.TryParse(MaxParallelDownloads, out int threads) || threads < 1 || threads > 10)
            {
                _viewService.Alert("Max Parallel Downloads must be an integer between 1 and 10.", "Error");
                return;
            }

            ConfigManager.DatabasePath = DatabasePath;
            ConfigManager.BackupDirectory = BackupDirectory;
            ConfigManager.LocalArchivePath = LocalArchivePath;
            ConfigManager.DownloadServer = DownloadServer;
            ConfigManager.MaxParallelDownloads = threads;
            ConfigManager.ForceLbp3Backups = ForceLbp3Backups;
            ConfigManager.Lbp2BetaToRetail = Lbp2BetaToRetail;
            ConfigManager.UseMemoryMappedIO = UseMemoryMappedIO;
            ConfigManager.LoadDbIntoRam = LoadDbIntoRam;
            ConfigManager.ShowExtractionSuccessPrompt = ShowExtractionSuccessPrompt;
            ConfigManager.Theme = SelectedTheme.Key ?? "DefaultTheme";
            ConfigManager.GameRegion = SelectedRegion.Substring(0, 2);

            await ConfigManager.SaveConfigAsync();
            RequestClose?.Invoke(true);
        }

        private void ExecuteCancel() => RequestClose?.Invoke(false);
    }
}