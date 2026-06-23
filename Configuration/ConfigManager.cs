using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LbpArchiveToolkit.Configuration
{
    /// <summary>
    /// Manages the persistence and loading of user preferences, UI states, and network routing logic.
    /// </summary>
    public static class ConfigManager
    {
        #region Configuration Properties

        public static string DatabasePath { get; set; } = "dry.db";
        public static string BackupDirectory { get; set; } = "backups";
        public static string DownloadServer { get; set; } = "zaprit";
        public static string LocalArchivePath { get; set; } = "";
        public static string Theme { get; set; } = "DefaultTheme";
        public static string GameRegion { get; set; } = "EU";
        public static int MaxParallelDownloads { get; set; } = 10;
        public static bool ForceLbp3Backups { get; set; } = false;
        public static bool Lbp2BetaToRetail { get; set; } = true;
        
        public static List<string> LegacySavedLevels { get; set; } = [];
        public static MainWindow.SearchState? LastSearch { get; set; }

        public static double WindowWidth { get; set; } = 1200;
        public static double WindowHeight { get; set; } = 700;
        public static double WindowLeft { get; set; } = -1;
        public static double WindowTop { get; set; } = -1;
        public static bool IsMaximized { get; set; } = false;

        #endregion

        #region Paths & Constants

        private static readonly System.Threading.Lock _saveLock = new();
        private static readonly SemaphoreSlim _saveLockAsync = new SemaphoreSlim(1, 1);

        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "LbpArchiveToolkit"
        );

        private static readonly string ConfigPath = Path.Combine(AppDataFolder, "config.json");
        private const string LegacyConfigPath = "config.json"; 

        #endregion

        #region Serialization Data Model

        /// <summary>
        /// A direct 1:1 schema used to map JSON files to the static manager state safely.
        /// </summary>
        private class ConfigData
        {
            public string? DatabasePath { get; set; }
            public string? BackupDirectory { get; set; }
            public string? DownloadServer { get; set; }
            public string? LocalArchivePath { get; set; }
            public string? Theme { get; set; }
            public string? GameRegion { get; set; }
            public int MaxParallelDownloads { get; set; }
            public bool ForceLbp3Backups { get; set; }
            public bool Lbp2BetaToRetail { get; set; }
            public double WindowWidth { get; set; }
            public double WindowHeight { get; set; }
            public double WindowLeft { get; set; }
            public double WindowTop { get; set; }
            public bool IsMaximized { get; set; }
            public List<string>? SavedLevels { get; set; }
            public MainWindow.SearchState? LastSearch { get; set; }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Reads the configuration file from disk. Will automatically migrate legacy config files 
        /// found in the app directory to the user's secure AppData folder.
        /// </summary>
        public static void LoadConfig()
        {
            if (!File.Exists(ConfigPath) && File.Exists(LegacyConfigPath))
            {
                try
                {
                    Directory.CreateDirectory(AppDataFolder);
                    File.Move(LegacyConfigPath, ConfigPath);
                }
                catch (Exception ex)
                {
                    LbpArchiveToolkit.LogManager.Log("ConfigManager.LoadConfig (Migration)", ex);
                }
            }

            string? pathToLoad = File.Exists(ConfigPath) ? ConfigPath : (File.Exists(LegacyConfigPath) ? LegacyConfigPath : null);
            if (pathToLoad == null) return;

            try
            {
                string json = File.ReadAllText(pathToLoad);
                var data = JsonSerializer.Deserialize<ConfigData>(json);

                if (data != null)
                {
                    DatabasePath = data.DatabasePath ?? DatabasePath;
                    BackupDirectory = data.BackupDirectory ?? BackupDirectory;
                    DownloadServer = data.DownloadServer ?? DownloadServer;
                    LocalArchivePath = data.LocalArchivePath ?? LocalArchivePath;
                    Theme = data.Theme ?? Theme;
                    GameRegion = data.GameRegion ?? GameRegion;
                    MaxParallelDownloads = data.MaxParallelDownloads;
                    ForceLbp3Backups = data.ForceLbp3Backups;
                    Lbp2BetaToRetail = data.Lbp2BetaToRetail;
                    WindowWidth = data.WindowWidth;
                    WindowHeight = data.WindowHeight;
                    WindowLeft = data.WindowLeft;
                    WindowTop = data.WindowTop;
                    IsMaximized = data.IsMaximized;
                    LegacySavedLevels = data.SavedLevels ?? new List<string>();
                    LastSearch = data.LastSearch;
                }
            }
            catch (Exception ex)
            {
                LbpArchiveToolkit.LogManager.Log("ConfigManager.LoadConfig (Parsing)", ex);
            }
        }

        /// <summary>
        /// Commits the current static state out to the configuration JSON file in AppData.
        /// </summary>
        public static void SaveConfig()
        {
            lock (_saveLock)
            {
                try
                {
                    var data = new ConfigData
                    {
                        DatabasePath = DatabasePath,
                        BackupDirectory = BackupDirectory,
                        DownloadServer = DownloadServer,
                        LocalArchivePath = LocalArchivePath,
                        Theme = Theme,
                        GameRegion = GameRegion,
                        MaxParallelDownloads = MaxParallelDownloads,
                        ForceLbp3Backups = ForceLbp3Backups,
                        Lbp2BetaToRetail = Lbp2BetaToRetail,
                        WindowWidth = WindowWidth,
                        WindowHeight = WindowHeight,
                        WindowLeft = WindowLeft,
                        WindowTop = WindowTop,
                        IsMaximized = IsMaximized,
                        LastSearch = LastSearch
                    };

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(data, options);

                    Directory.CreateDirectory(AppDataFolder);
                    string tempPath = ConfigPath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, ConfigPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    LbpArchiveToolkit.LogManager.Log("ConfigManager.SaveConfig", ex);
                }
            }
        }

        /// <summary>
        /// Commits the current static state out to the configuration JSON file in AppData asynchronously.
        /// </summary>
        public static async Task SaveConfigAsync()
        {
            await _saveLockAsync.WaitAsync().ConfigureAwait(false);
            try
            {
                var data = new ConfigData
                {
                    DatabasePath = DatabasePath,
                    BackupDirectory = BackupDirectory,
                    DownloadServer = DownloadServer,
                    LocalArchivePath = LocalArchivePath,
                    Theme = Theme,
                    GameRegion = GameRegion,
                    MaxParallelDownloads = MaxParallelDownloads,
                    ForceLbp3Backups = ForceLbp3Backups,
                    Lbp2BetaToRetail = Lbp2BetaToRetail,
                    WindowWidth = WindowWidth,
                    WindowHeight = WindowHeight,
                    WindowLeft = WindowLeft,
                    WindowTop = WindowTop,
                    IsMaximized = IsMaximized,
                    LastSearch = LastSearch
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, options);

                Directory.CreateDirectory(AppDataFolder);
                string tempPath = ConfigPath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
                File.Move(tempPath, ConfigPath, overwrite: true);
            }
            catch (Exception ex)
            {
                LbpArchiveToolkit.LogManager.Log("ConfigManager.SaveConfigAsync", ex);
            }
            finally
            {
                _saveLockAsync.Release();
            }
        }

        #endregion
    }
}
