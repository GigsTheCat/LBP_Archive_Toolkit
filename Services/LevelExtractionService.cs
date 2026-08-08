using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace LbpArchiveToolkit.Services
{
    public static class LevelExtractionService
    {
        public static async Task ExtractLevelsAsync(List<LevelItem> levelsToExtract, Action<LevelItem>? onLevelSaved = null, string? customBackupDir = null)
        {
            var owner = Application.Current.MainWindow;
            var progressWin = new ProgressWindow { Owner = owner };
            progressWin.Show();
            if (owner != null) owner.IsEnabled = false;

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

                        var result = await AssetDownloader.RunExtractionProcessAsync(lvl, ConfigManager.DatabasePath, customBackupDir ?? ConfigManager.BackupDirectory, MainWindow.SharedHttpClient, config, token, progressIndicator);

                        if (result is ExtractionResult.Success)
                        {
                            successCount++;

                            if (!SavedLevelsManager.Contains(lvl.Id.ToString()))
                            {
                                SavedLevelsManager.SavedLevels.Add(lvl.Id.ToString());
                            }

                            onLevelSaved?.Invoke(lvl);
                        }
                        else if (result is ExtractionResult.Error err)
                        {
                            if (err.Message.Contains("cancelled")) wasCancelled = true;
                            else
                            {
                                failureCount++;
                                errorMessages.Add($"'{lvl.LevelName}': {err.Message}");
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
                }
            }
            finally
            {
                progressWin.Close();
                if (owner != null) owner.IsEnabled = true;
                AssetDownloader.CleanupLocalArchives();

                // Force a full garbage collection and compact the Large Object Heap - Probably unnecessary
                // System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                // GC.Collect(2, GCCollectionMode.Forced, true, true);
                // GC.WaitForPendingFinalizers();
            }

            if (failureCount > 0)
            {
                string errors = string.Join("\n\n", errorMessages);
                CustomDialog.Show(owner!, $"Failed to download/pack {failureCount} level(s).\n\nReasons:\n{errors}", "Extraction Failed", false);
            }

            if (successCount > 0)
            {
                if (ConfigManager.ShowExtractionSuccessPrompt)
                {
                    string msg = wasCancelled ? $"Cancelled! However, {successCount} level(s) were successfully packed before cancellation.\n\nOpen backup folder?"
                                              : $"Successfully packed {successCount} level(s)!\n\nOpen backup folder?";

                    bool dontShowAgain = false;
                    bool result = CustomDialog.ShowWithCheckbox(owner!, msg, "Finished", "Don't show again", out dontShowAgain, true);

                    if (dontShowAgain)
                    {
                        ConfigManager.ShowExtractionSuccessPrompt = false;
                        ConfigManager.SaveConfig();
                    }

                    if (result)
                    {
                        string fullPath = Path.GetFullPath(customBackupDir ?? ConfigManager.BackupDirectory);
                        if (Directory.Exists(fullPath)) Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true, Verb = "open" });
                    }
                }
                else
                {
                    if (Application.Current.MainWindow is LbpArchiveToolkit.ViewModels.IViewService viewService)
                    {
                        string msg = wasCancelled ? $"Packed {successCount} level(s) before cancellation" : $"Successfully packed {successCount} level(s)!";
                        viewService.ShowToast(msg, "btnExtract");
                    }
                }
            }
        }
    }
}