using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Services;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit
{
    public partial class HeartedCreatorsWindow : Window
    {
        public ObservableCollection<UserItem> HeartedList { get; set; } = new();
        
        private CancellationTokenSource? _iconCts;
        private long _currentIconRequestId = -1;

        public HeartedCreatorsWindow()
        {
            InitializeComponent();
            lvHearted.ItemsSource = HeartedList;
            
            LoadHeartedCreators();
            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
        }

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        private void LoadHeartedCreators()
        {
            HeartedList.Clear();
            foreach (var item in HeartedCreatorsManager.HeartedCreators)
            {
                HeartedList.Add(item);
            }
            txtStatus.Text = $"You have {HeartedList.Count} hearted creator(s).";
            iconHeartOverlay.Visibility = Visibility.Hidden;

            if (HeartedList.Any())
            {
                lvHearted.SelectedIndex = 0;
            }
        }

        private async void LvHearted_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            btnRemove.IsEnabled = lvHearted.SelectedItems.Count > 0;
            btnViewUserLevels.IsEnabled = lvHearted.SelectedItems.Count > 0;
            btnDownloadAllLevels.IsEnabled = lvHearted.SelectedItems.Count > 0;

            if (lvHearted.SelectedItem is UserItem selected)
            {
                txtUserNpHandle.Text = selected.NpHandle;
                txtUserStats.Text = $"Hearts: {selected.HeartCount}  |  Total Levels: {selected.TotalLevels}";
                txtUserSummary.Text = $"Published Level slots summary:\n" +
                                      $"• LBP1 Slots: {selected.Lbp1UsedSlots}\n" +
                                      $"• LBP2 Slots: {selected.Lbp2UsedSlots}\n" +
                                      $"• LBP3 Slots: {selected.Lbp3UsedSlots}";
                iconHeartOverlay.Visibility = Visibility.Visible;
                
                _currentIconRequestId = selected.NpHandle.GetHashCode();
                _iconCts?.Cancel();
                _iconCts?.Dispose();
                _iconCts = new CancellationTokenSource();
                
                await LoadUserIconAsync(selected.IconHash, selected.NpHandle, _iconCts.Token);
            } 
            else
            {
                txtUserNpHandle.Text = "";
                txtUserStats.Text = "";
                txtUserSummary.Text = "";
                iconHeartOverlay.Visibility = Visibility.Hidden;
                iconEllipse.Fill = (Brush)FindResource("BgPrimary");
                txtIconStatus.Text = "Select a creator\nto view details";
            }
        }

        private async Task LoadUserIconAsync(string? hash, string npHandle, CancellationToken token)
        {
            iconEllipse.Fill = (Brush)FindResource("BgPrimary");

            if (string.IsNullOrEmpty(hash) || hash.Length <= 8)
            {
                txtIconStatus.Text = "No Icon Available";
                return;
            }

            txtIconStatus.Text = "Loading Icon...";
            long expectedRequestId = npHandle.GetHashCode();

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
            var selectedItems = lvHearted.SelectedItems.Cast<UserItem>().ToList();
            if (!selectedItems.Any()) return;

            bool isConfirmed = CustomDialog.Show(
                this, 
                $"Are you sure you want to remove {selectedItems.Count} creator(s) from your hearted list?", 
                "Confirm Removal", 
                isYesNo: true);

            if (isConfirmed)
            {
                lvHearted.SelectedIndex = -1;
                foreach (var item in selectedItems)
                {
                    HeartedCreatorsManager.Remove(item.NpHandle);
                    HeartedList.Remove(item);
                }
                txtStatus.Text = $"Removed {selectedItems.Count} creator(s).";

                if (HeartedList.Any())
                {
                    lvHearted.SelectedIndex = 0;
                }
            }
        }

        private void BtnViewUserLevels_Click(object sender, RoutedEventArgs e)
        {
            if (lvHearted.SelectedItem is UserItem selectedUser && this.Owner is MainWindow mainWindow)
            {
                this.Close();
                mainWindow.InitiateCreatorSearch(selectedUser.NpHandle);
            }
        }

        private async void BtnDownloadAllLevels_Click(object sender, RoutedEventArgs e)
        {
            if (lvHearted.SelectedItem is UserItem selectedUser && this.Owner is MainWindow mainWindow)
            {
                this.Close();
                await mainWindow.InitiateBatchDownloadAsync(selectedUser);
            }
        }

            }
}