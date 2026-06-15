using System.IO;
using System.Windows;
using Microsoft.Win32;
using LbpArchiveToolkit.Configuration;

namespace LbpArchiveToolkit
{
    /// <summary>
    /// Handles user configuration, directory routing, and app-wide preference management.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        #region Initialization & Lifecycle

        public SettingsWindow()
        {
            InitializeComponent();
            LoadConfigToUI();
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
            chkFixVersion.IsChecked = ConfigManager.FixBackupVersion;
            chkForceLbp3.IsChecked = ConfigManager.ForceLbp3Backups;
            chkLbp2Beta.IsChecked = ConfigManager.Lbp2BetaToRetail;
        }

        #endregion

        #region Custom Title Bar Controls

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) 
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        #endregion

        #region File & Folder Browsing

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
            }
        }

        private void BtnBrowseBackup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog 
            { 
                ValidateNames = false, 
                CheckFileExists = false, 
                CheckPathExists = true, 
                FileName = "Folder Selection.", 
                Title = "Select Backup Directory" 
            };
            
            if (dialog.ShowDialog() == true)
            {
                txtBackupDir.Text = Path.GetDirectoryName(dialog.FileName);
            }
        }
        
        private void BtnBrowseLocalArchive_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog 
            { 
                ValidateNames = false, 
                CheckFileExists = false, 
                CheckPathExists = true, 
                FileName = "Folder Selection.", 
                Title = "Select Local Archive Directory" 
            };
            
            if (dialog.ShowDialog() == true)
            {
                txtLocalArchive.Text = Path.GetDirectoryName(dialog.FileName);
            }
        }

        #endregion

        #region Configuration Actions

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
                    ConfigManager.SavedLevels.Clear();
                    ConfigManager.SaveConfig();
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
                chkFixVersion.IsChecked = true;
                chkForceLbp3.IsChecked = false;
                chkLbp2Beta.IsChecked = true;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtThreads.Text, out int threads) || threads < 1 || threads > 10)
            {
                CustomDialog.Show(this, "Max Parallel Downloads must be an integer between 1 and 10.", "Error", isYesNo: false);
                return;
            }

            ConfigManager.DatabasePath = txtDbPath.Text;
            ConfigManager.BackupDirectory = txtBackupDir.Text;
            ConfigManager.LocalArchivePath = txtLocalArchive.Text;
            ConfigManager.DownloadServer = (cmbServer.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "bonsai";
            ConfigManager.MaxParallelDownloads = threads;
            ConfigManager.FixBackupVersion = chkFixVersion.IsChecked == true;
            ConfigManager.ForceLbp3Backups = chkForceLbp3.IsChecked == true;
            ConfigManager.Lbp2BetaToRetail = chkLbp2Beta.IsChecked == true;

            ConfigManager.SaveConfig();
            this.DialogResult = true; 
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => this.Close();

        #endregion
    }
}