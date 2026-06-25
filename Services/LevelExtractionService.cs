using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;

namespace LbpArchiveToolkit.Services
{
    public static class LevelExtractionService
    {
        public static async Task ExtractLevelsAsync(Window owner, List<LevelItem> levelsToExtract, Action<LevelItem>? onLevelSaved = null)
        {
            var progressWin = new ProgressWindow { Owner = owner };
            progressWin.Show();
            owner.IsEnabled = false;

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

                        var result = await AssetDownloader.RunExtractionProcessAsync(lvl, ConfigManager.DatabasePath, ConfigManager.BackupDirectory, MainWindow.SharedHttpClient, config, token, progressIndicator);
                        
                        if (result.Success)
                        {
                            successCount++;
                            
                            if (!SavedLevelsManager.Contains(lvl.Id.ToString()))
                            {
                                SavedLevelsManager.SavedLevels.Add(lvl.Id.ToString());
                            }
                            
                            onLevelSaved?.Invoke(lvl);
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
                }
            }
            finally
            {
                progressWin.Close();
                owner.IsEnabled = true;
                AssetDownloader.CleanupLocalArchives();

                // Force a full garbage collection and compact the Large Object Heap
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
            }

            if (failureCount > 0)
            {
                string errors = string.Join("\n\n", errorMessages);
                CustomDialog.Show(owner, $"Failed to download/pack {failureCount} level(s).\n\nReasons:\n{errors}", "Extraction Failed", false);
            }

            if (successCount > 0)
            {
                string msg = wasCancelled ? $"Cancelled! However, {successCount} level(s) were successfully packed before cancellation.\n\nOpen backup folder?" 
                                          : $"Successfully packed {successCount} level(s)!\n\nOpen backup folder?";

                if (CustomDialog.Show(owner, msg, "Finished", true))
                {
                    string fullPath = Path.GetFullPath(ConfigManager.BackupDirectory);
                    if (Directory.Exists(fullPath)) Process.Start("explorer.exe", $"\"{fullPath}\"");
                }
            }
        }
    }
}