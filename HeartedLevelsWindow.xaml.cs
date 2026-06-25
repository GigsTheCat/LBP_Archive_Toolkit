using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit
{
    public partial class HeartedLevelsWindow : Window
    {
        public ObservableCollection<LevelItem> HeartedList { get; set; } = new();
        
        private CancellationTokenSource? _iconCts;
        private long _currentIconRequestId = -1;

        public HeartedLevelsWindow()
        {
            InitializeComponent();
            lvHearted.ItemsSource = HeartedList;
            
            LoadHeartedLevels();
            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
        }

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        private void LoadHeartedLevels()
        {
            HeartedList.Clear();
            foreach (var item in HeartedLevelsManager.HeartedLevels)
            {
                HeartedList.Add(item);
            }
            txtStatus.Text = $"You have {HeartedList.Count} hearted level(s).";
            iconHeartOverlay.Visibility = Visibility.Hidden;

            if (HeartedList.Any())
            {
                lvHearted.SelectedIndex = 0;
            }
        }

        private async void LvHearted_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnRemove.IsEnabled = lvHearted.SelectedItems.Count > 0;
            btnExtract.IsEnabled = lvHearted.SelectedItems.Count > 0;

            if (lvHearted.SelectedItem is LevelItem selected)
            {
                txtLevelTitle.Text = selected.LevelName;
                txtDescription.Text = selected.Description;
                txtCreator.Text = $"By: {selected.Creator}  |  Game: {selected.Game}";
                iconHeartOverlay.Visibility = Visibility.Visible;
                
                mmPickTails.Visibility = selected.IsMmPick ? Visibility.Visible : Visibility.Hidden;
                mmPickRosette.Visibility = selected.IsMmPick ? Visibility.Visible : Visibility.Hidden;
                mmPickRosetteInner.Visibility = selected.IsMmPick ? Visibility.Visible : Visibility.Hidden;
                iconEllipse.Stroke = selected.IsMmPick ? (Brush)FindResource("LbpPink") : (Brush)FindResource("LbpOrange");

                _currentIconRequestId = selected.Id;
                _iconCts?.Cancel();
                _iconCts?.Dispose();
                _iconCts = new CancellationTokenSource();
                
                await LoadIconAsync(selected.IconHash, _iconCts.Token);
            } 
            else
            {
                txtLevelTitle.Text = "";
                txtDescription.Text = "";
                txtCreator.Text = "";
                iconHeartOverlay.Visibility = Visibility.Hidden;
                mmPickTails.Visibility = Visibility.Hidden;
                mmPickRosette.Visibility = Visibility.Hidden;
                mmPickRosetteInner.Visibility = Visibility.Hidden;
                iconEllipse.Stroke = (Brush)FindResource("LbpOrange");
                iconEllipse.Fill = (Brush)FindResource("BgPrimary");
                txtIconStatus.Text = "Select a level\nto view details";
            }
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

            var brush = await LbpArchiveToolkit.Services.IconLoaderService.LoadIconBrushAsync(hash, MainWindow.SharedHttpClient, token);

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

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = lvHearted.SelectedItems.Cast<LevelItem>().ToList();
            if (!selectedItems.Any()) return;

            bool isConfirmed = CustomDialog.Show(
                this, 
                $"Are you sure you want to remove {selectedItems.Count} level(s) from your hearted list?", 
                "Confirm Removal", 
                isYesNo: true);

            if (isConfirmed)
            {
                lvHearted.SelectedIndex = -1;
                foreach (var item in selectedItems)
                {
                    HeartedLevelsManager.Remove(item.Id);
                    HeartedList.Remove(item);
                }
                txtStatus.Text = $"Removed {selectedItems.Count} level(s).";

                if (HeartedList.Any())
                {
                    lvHearted.SelectedIndex = 0;
                }
            }
        }

        private async void BtnExtract_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = lvHearted.SelectedItems.Cast<LevelItem>().ToList();
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
                            lvl.Saved = "✓";
                            
                            if (!SavedLevelsManager.Contains(lvl.Id.ToString()))
                            {
                                SavedLevelsManager.SavedLevels.Add(lvl.Id.ToString());
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
                    SavedLevelsManager.Save();
                }
            }
            finally
            {
                progressWin.Close();
                this.IsEnabled = true;
                AssetDownloader.CleanupLocalArchives();

                // Force a full garbage collection and compact the Large Object Heap
                // to immediately release memory claimed during massive batch processes.
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
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
                    if (Directory.Exists(fullPath)) Process.Start("explorer.exe", $"\"{fullPath}\"");
                }
            }
        }

            }
}