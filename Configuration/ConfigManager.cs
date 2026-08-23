using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using LbpArchiveToolkit.Models;

namespace LbpArchiveToolkit.Configuration
{
    /// <summary>
    /// Manages the persistence and loading of user preferences, UI states, and network routing logic.
    /// </summary>
    public static partial class ConfigManager
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
        public static bool ForceLbp3Ps4Backups { get; set; } = false;
        public static bool Lbp2BetaToRetail { get; set; } = true;
        public static bool UseMemoryMappedIO { get; set; } = false;
        public static bool LoadDbIntoRam { get; set; } = false;
        public static bool ShowExtractionSuccessPrompt { get; set; } = true;
        public static bool EnableAutocomplete { get; set; } = true;

        public static List<string> LegacySavedLevels { get; set; } = [];
        
        public static SearchState? LastSearch { get; set; }

        public static double WindowWidth { get; set; } = 1250;
        public static double WindowHeight { get; set; } = 720;
        public static double WindowLeft { get; set; } = -1;
        public static double WindowTop { get; set; } = -1;
        public static bool IsMaximized { get; set; } = false;
        public static DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;

        #endregion

        #region Paths & Constants

        private static readonly System.Threading.Lock _saveLock = new();

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
        [JsonSerializable(typeof(ConfigData))]
        [JsonSerializable(typeof(SearchState))]
        [JsonSerializable(typeof(LevelItem))]
        [JsonSerializable(typeof(UserItem))]
        [JsonSerializable(typeof(AdvancedSearchCriteria))]
        internal partial class ConfigJsonContext : JsonSerializerContext { }

        internal class ConfigData
        {
            public string? DatabasePath { get; set; }
            public string? BackupDirectory { get; set; }
            public string? DownloadServer { get; set; }
            public string? LocalArchivePath { get; set; }
            public string? Theme { get; set; }
            public string? GameRegion { get; set; }
            public int MaxParallelDownloads { get; set; }
            public bool ForceLbp3Backups { get; set; }
            public bool ForceLbp3Ps4Backups { get; set; }
            public bool? Lbp2BetaToRetail { get; set; }
            public bool UseMemoryMappedIO { get; set; }
            public bool LoadDbIntoRam { get; set; }
            public bool? ShowExtractionSuccessPrompt { get; set; }
            public bool? EnableAutocomplete { get; set; }
            public double WindowWidth { get; set; }
            public double WindowHeight { get; set; }
            public double WindowLeft { get; set; }
            public double WindowTop { get; set; }
            public bool IsMaximized { get; set; }
            public DateTime LastUpdateCheck { get; set; }
            public List<string>? SavedLevels { get; set; }
            
            public SearchState? LastSearch { get; set; }
        }

        #endregion

        #region Public API

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
                using var fs = new FileStream(pathToLoad, FileMode.Open, FileAccess.Read, FileShare.Read);
                var data = JsonSerializer.Deserialize(fs, ConfigJsonContext.Default.ConfigData);

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
                    ForceLbp3Ps4Backups = data.ForceLbp3Ps4Backups;
                    Lbp2BetaToRetail = data.Lbp2BetaToRetail ?? Lbp2BetaToRetail;
                    UseMemoryMappedIO = data.UseMemoryMappedIO;
                    LoadDbIntoRam = data.LoadDbIntoRam;
                    ShowExtractionSuccessPrompt = data.ShowExtractionSuccessPrompt ?? ShowExtractionSuccessPrompt;
                    EnableAutocomplete = data.EnableAutocomplete ?? EnableAutocomplete;
                    WindowWidth = data.WindowWidth;
                    WindowHeight = data.WindowHeight;
                    WindowLeft = data.WindowLeft;
                    WindowTop = data.WindowTop;
                    IsMaximized = data.IsMaximized;
                    LastUpdateCheck = data.LastUpdateCheck;
                    LegacySavedLevels = data.SavedLevels ?? new List<string>();
                    LastSearch = data.LastSearch;
                }
            }
            catch (Exception ex)
            {
                LbpArchiveToolkit.LogManager.Log("ConfigManager.LoadConfig (Parsing)", ex);
            }
        }

        public static void SaveConfig()
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
                    ForceLbp3Ps4Backups = ForceLbp3Ps4Backups,
                    Lbp2BetaToRetail = Lbp2BetaToRetail,
                    UseMemoryMappedIO = UseMemoryMappedIO,
                    LoadDbIntoRam = LoadDbIntoRam,
                    ShowExtractionSuccessPrompt = ShowExtractionSuccessPrompt,
                    EnableAutocomplete = EnableAutocomplete,
                    WindowWidth = WindowWidth,
                    WindowHeight = WindowHeight,
                    WindowLeft = WindowLeft,
                    WindowTop = WindowTop,
                    IsMaximized = IsMaximized,
                    LastUpdateCheck = LastUpdateCheck,
                    SavedLevels = LegacySavedLevels,
                    LastSearch = LastSearch
                };

                lock (_saveLock)
                {
                    Directory.CreateDirectory(AppDataFolder);
                    string tempPath = ConfigPath + ".tmp";
                    
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var options = new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = ConfigJsonContext.Default };
                        JsonSerializer.Serialize(fs, data, options);
                    }
                    
                    File.Move(tempPath, ConfigPath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                LbpArchiveToolkit.LogManager.Log("ConfigManager.SaveConfig", ex);
            }
        }

        public static Task SaveConfigAsync()
        {
            return Task.Run(SaveConfig);
        }

        #endregion
    }
}