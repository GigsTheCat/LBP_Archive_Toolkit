using System.IO;
using System.Windows;
using System.Diagnostics;
using Microsoft.Win32;
using Microsoft.Data.Sqlite;
using LbpArchiveToolkit.Configuration;

namespace LbpArchiveToolkit
{
    /// <summary>
    /// Handles user configuration, directory routing, and app-wide preference management.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        #region Initialization & Lifecycle

        private string _originalTheme = "DefaultTheme";
        private bool _isInitialized = false;

        public SettingsWindow()
        {
            InitializeComponent();
            _originalTheme = ConfigManager.Theme;
            LoadConfigToUI();
            _isInitialized = true;
        }

        private void LoadConfigToUI()
        {
            txtDbPath.Text = ConfigManager.DatabasePath;
            txtBackupDir.Text = ConfigManager.BackupDirectory;
            txtLocalArchive.Text = ConfigManager.LocalArchivePath;
            
            foreach (System.Windows.Controls.ComboBoxItem item in cmbServer.Items)
            {
                if (item.Content.ToString() == ConfigManager.DownloadServer)
                {
                    cmbServer.SelectedItem = item;
                    break;
                }
            }

            txtThreads.Text = ConfigManager.MaxParallelDownloads.ToString();
            chkForceLbp3.IsChecked = ConfigManager.ForceLbp3Backups;
            chkLbp2Beta.IsChecked = ConfigManager.Lbp2BetaToRetail;
            chkUseMmap.IsChecked = ConfigManager.UseMemoryMappedIO;
            chkLoadDbIntoRam.IsChecked = ConfigManager.LoadDbIntoRam;

            // Dynamically populate available themes from the ThemeManager
            cmbTheme.Items.Clear();
            foreach (var theme in LbpArchiveToolkit.Themes.ThemeManager.AvailableThemes)
            {
                var item = new System.Windows.Controls.ComboBoxItem
                {
                    Content = theme.Value,
                    Tag = theme.Key
                };
                cmbTheme.Items.Add(item);

                if (theme.Key == ConfigManager.Theme)
                {
                    cmbTheme.SelectedItem = item;
                }
            }

            foreach (System.Windows.Controls.ComboBoxItem item in cmbRegion.Items)
            {
                if (item.Content.ToString()!.StartsWith(ConfigManager.GameRegion))
                {
                    cmbRegion.SelectedItem = item;
                    break;
                }
            }
            if (cmbRegion.SelectedItem == null) cmbRegion.SelectedIndex = 1;
        }

        #endregion

        

        #region Custom Title Bar Controls

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) 
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        #endregion

        #region File & Folder Browsing

         private bool _promptedForDb = false;
 
         private void CheckFtsSupport(string dbPath)
 {
     if (_promptedForDb || !File.Exists(dbPath)) return;
     
     try
     {
         var connStringBuilder = new SqliteConnectionStringBuilder { DataSource = dbPath };
         using var conn = new SqliteConnection(connStringBuilder.ConnectionString);
         conn.Open();
         using var cmdFts = new SqliteCommand("SELECT count(*) FROM sqlite_master WHERE type='table' AND name='slot_fts'", conn);
                 bool hasFts = System.Convert.ToInt32(cmdFts.ExecuteScalar()) > 0;
                 if (!hasFts)
                 {
                     _promptedForDb = true;
                     bool download = CustomDialog.Show(this, "The selected database does not support FTS5 hardware acceleration. Searching will be much slower.\n\nWould you like to download the newer, faster version?", "Outdated Database", isYesNo: true);
                     if (download)
                     {
                         Process.Start(new ProcessStartInfo("https://archive.org/download/fastdry") { UseShellExecute = true });
                     }
                 }
             }
             catch (Exception ex)
             {
                 LbpArchiveToolkit.LogManager.Log("SettingsWindow.CheckFtsSupport", ex);
             }
         }
 

        private void BtnBrowseDb_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog 
            { 
                Filter = "Database Files (*.db)|*.db|All Files (*.*)|*.*", 
                Title = "Select dry.db file" 
            };

            if (dialog.ShowDialog() == true) 
            {
                txtDbPath.Text = dialog.FileName;
                CheckFtsSupport(dialog.FileName);
            }
        }

        private void BtnBrowseBackup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog 
            { 
                Title = "Select Backup Directory" 
            };
            
            if (dialog.ShowDialog() == true)
            {
                txtBackupDir.Text = dialog.FolderName;
            }
        }
        
        private void BtnBrowseLocalArchive_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog 
            { 
                Title = "Select Local Archive Directory" 
            };
            
            if (dialog.ShowDialog() == true)
            {
                txtLocalArchive.Text = dialog.FolderName;
            }
        }

        #endregion

        #region Configuration Actions

        private void CmbTheme_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;

            if (cmbTheme.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                string themeName = selectedItem.Tag?.ToString() ?? "DefaultTheme";
                LbpArchiveToolkit.Themes.ThemeManager.ApplyTheme(themeName);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (this.DialogResult != true)
            {
                // Revert to original theme if closed without saving
                LbpArchiveToolkit.Themes.ThemeManager.ApplyTheme(_originalTheme);
            }
            base.OnClosed(e);
        }

        private void BtnForgetLevels_Click(object sender, RoutedEventArgs e)
        {
            bool result = CustomDialog.Show(
                this, 
                "Are you sure you want to forget all saved levels?\nThis takes effect immediately and cannot be undone.", 
                "Forget Saved Levels", 
                isYesNo: true);
            
            if (result)
            {
                if (this.Owner is MainWindow main)
                {
                    main.ClearSavedLevels();
                }
                else
                {
                    SavedLevelsManager.Clear();
                }
                
                CustomDialog.Show(this, "All saved levels have been cleared.", "Success", isYesNo: false);
            }
        }

        private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            bool result = CustomDialog.Show(
                this, 
                "Reset all configuration fields to their default values?\n(You still need to click 'Save Settings' to apply this change.)", 
                "Reset to Default", 
                isYesNo: true);
            
            if (result)
            {
                txtDbPath.Text = "dry.db";
                txtBackupDir.Text = "backups";
                txtLocalArchive.Text = "";
                
                foreach (System.Windows.Controls.ComboBoxItem item in cmbServer.Items)
                {
                    if (item.Content.ToString() == "bonsai")
                    {
                        cmbServer.SelectedItem = item;
                        break;
                    }
                }

                txtThreads.Text = "10";
                chkForceLbp3.IsChecked = false;
                chkLbp2Beta.IsChecked = true;
                chkUseMmap.IsChecked = false;
                chkLoadDbIntoRam.IsChecked = false;

                foreach (System.Windows.Controls.ComboBoxItem item in cmbRegion.Items)
                {
                    if (item.Content.ToString()!.StartsWith("EU"))
                    {
                        cmbRegion.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtThreads.Text, out int threads) || threads < 1 || threads > 10)
            {
                CustomDialog.Show(this, "Max Parallel Downloads must be an integer between 1 and 10.", "Error", isYesNo: false);
                return;
            }

            if (txtDbPath.Text != ConfigManager.DatabasePath)
            {
                CheckFtsSupport(txtDbPath.Text);
            }
    
            ConfigManager.DatabasePath = txtDbPath.Text;
            ConfigManager.BackupDirectory = txtBackupDir.Text;
            ConfigManager.LocalArchivePath = txtLocalArchive.Text;
            ConfigManager.DownloadServer = (cmbServer.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "bonsai";
            ConfigManager.MaxParallelDownloads = threads;
            ConfigManager.ForceLbp3Backups = chkForceLbp3.IsChecked == true;
            ConfigManager.Lbp2BetaToRetail = chkLbp2Beta.IsChecked == true;
            ConfigManager.UseMemoryMappedIO = chkUseMmap.IsChecked == true;
            ConfigManager.LoadDbIntoRam = chkLoadDbIntoRam.IsChecked == true;

            // Save selected theme configuration
            if (cmbTheme.SelectedItem is System.Windows.Controls.ComboBoxItem selectedThemeItem)
            {
                ConfigManager.Theme = selectedThemeItem.Tag?.ToString() ?? "DefaultTheme";
            }

            string regionStr = (cmbRegion.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "EU";
            ConfigManager.GameRegion = regionStr.Substring(0, 2);

            await ConfigManager.SaveConfigAsync();
            this.DialogResult = true; 
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => this.Close();

        #endregion
    }
}