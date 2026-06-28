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
        private long _iconRequestCounter = 0;
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

                _currentIconRequestId = Interlocked.Increment(ref _iconRequestCounter);
                _iconCts?.Cancel();
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

            await LevelExtractionService.ExtractLevelsAsync(this, selectedItems, lvl => 
            {
                lvl.Saved = "✓";
            });
            
            txtStatus.Text = "Extraction finished.";
        }

            }
}