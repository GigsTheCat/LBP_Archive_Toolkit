using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace LbpArchiveToolkit
{
    public partial class LogViewerWindow : Window
    {
        private readonly string _logFolder;

        public LogViewerWindow()
        {
            InitializeComponent();
            _logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LbpArchiveToolkit");
            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
            LoadSelectedLog();
        }

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        private void CmbLogType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.IsLoaded)
            {
                LoadSelectedLog();
            }
        }

        private async void LoadSelectedLog()
        {
            if (cmbLogType.SelectedItem is ComboBoxItem selectedItem)
            {
                string fileName = selectedItem.Tag?.ToString() ?? "debug_log.txt";
                string filePath = Path.Combine(_logFolder, fileName);

                txtLogDisplay.Text = "Reading log file...";

                try
                {
                    string logText = await Task.Run(() =>
                    {
                        if (!File.Exists(filePath))
                        {
                            return "No entries have been recorded yet.";
                        }

                        try
                        {
                            // Opens the stream safely allowing other active threads to write without triggering crash errors
                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var sr = new StreamReader(fs, Encoding.UTF8))
                            {
                                return sr.ReadToEnd();
                            }
                        }
                        catch (Exception ex)
                        {
                            return $"Failed to read file:\n{ex.Message}";
                        }
                    });

                    txtLogDisplay.Text = string.IsNullOrWhiteSpace(logText) ? "File is currently empty." : logText;
                    txtLogDisplay.ScrollToEnd();
                }
                catch (Exception ex)
                {
                    txtLogDisplay.Text = $"An error occurred: {ex.Message}";
                }
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(_logFolder))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{_logFolder}\"");
                }
                else
                {
                    CustomDialog.Show(this, "Log directory does not exist yet.", "Notice");
                }
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, $"Failed to open directory:\n{ex.Message}", "Error");
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(txtLogDisplay.Text))
                {
                    Clipboard.SetText(txtLogDisplay.Text);
                    CustomDialog.Show(this, "Log contents copied to clipboard.", "Success");
                }
            }
            catch (Exception ex)
            {
                CustomDialog.Show(this, $"Failed to copy text:\n{ex.Message}", "Error");
            }
        }

        private async void BtnErase_Click(object sender, RoutedEventArgs e)
        {
            if (cmbLogType.SelectedItem is ComboBoxItem selectedItem)
            {
                string fileName = selectedItem.Tag?.ToString() ?? "debug_log.txt";
                string filePath = Path.Combine(_logFolder, fileName);

                bool confirm = CustomDialog.Show(this, $"Are you sure you want to erase the {selectedItem.Content}? This action cannot be undone.", "Erase Log File", isYesNo: true);
                if (confirm)
                {
                    try
                    {
                        await Task.Run(() =>
                        {
                            if (File.Exists(filePath))
                            {
                                // Truncates the file to 0 bytes safely instead of deleting to bypass occasional thread handles locks
                                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                                {
                                }
                            }
                        });

                        LoadSelectedLog();
                    }
                    catch (Exception ex)
                    {
                        CustomDialog.Show(this, $"Failed to erase file:\n{ex.Message}", "Error");
                    }
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}