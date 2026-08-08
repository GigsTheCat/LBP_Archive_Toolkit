using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class LogViewerWindowViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;
        private readonly string _logFolder;

        public int SelectedLogIndex
        {
            get;
            set
            {
                if (SetProperty(ref field, value))
                {
                    LoadSelectedLog();
                }
            }
        } = 0;

        public string LogText { get; set => SetProperty(ref field, value); } = "Reading log file...";

        public ICommand OpenFolderCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand EraseCommand { get; }

        public LogViewerWindowViewModel(IViewService viewService)
        {
            _viewService = viewService;
            _logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LbpArchiveToolkit");

            OpenFolderCommand = new RelayCommand(ExecuteOpenFolder);
            CopyCommand = new RelayCommand(ExecuteCopy);
            EraseCommand = new RelayCommand(ExecuteErase);

            LoadSelectedLog();
        }

        private async void LoadSelectedLog()
        {
            string fileName = SelectedLogIndex == 0 ? "debug_log.txt" : "crash_log.txt";
            string filePath = Path.Combine(_logFolder, fileName);

            LogText = "Reading log file...";

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
                        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var sr = new StreamReader(fs, Encoding.UTF8);
                        return sr.ReadToEnd();
                    }
                    catch (Exception ex)
                    {
                        return $"Failed to read file:\n{ex.Message}";
                    }
                });

                LogText = string.IsNullOrWhiteSpace(logText) ? "File is currently empty." : logText;
            }
            catch (Exception ex)
            {
                LogText = $"An error occurred: {ex.Message}";
            }
        }

        private void ExecuteOpenFolder(object? parameter)
        {
            try
            {
                if (Directory.Exists(_logFolder))
                {
                    _viewService.OpenDirectory(_logFolder);
                }
                else
                {
                    _viewService.Alert("Log directory does not exist yet.", "Notice");
                }
            }
            catch (Exception ex)
            {
                _viewService.Alert($"Failed to open directory:\n{ex.Message}", "Error");
            }
        }

        private void ExecuteCopy(object? parameter)
        {
            try
            {
                if (!string.IsNullOrEmpty(LogText))
                {
                    _viewService.SetClipboardText(LogText);
                    _viewService.Alert("Log contents copied to clipboard.", "Success");
                }
            }
            catch (Exception ex)
            {
                _viewService.Alert($"Failed to copy text:\n{ex.Message}", "Error");
            }
        }

        private async void ExecuteErase(object? parameter)
        {
            string logName = SelectedLogIndex == 0 ? "Debug Log" : "Crash Log";
            string fileName = SelectedLogIndex == 0 ? "debug_log.txt" : "crash_log.txt";
            string filePath = Path.Combine(_logFolder, fileName);

            bool confirm = _viewService.Confirm($"Are you sure you want to erase the {logName}? This action cannot be undone.", "Erase Log File");
            if (confirm)
            {
                try
                {
                    await Task.Run(() =>
                    {
                        if (File.Exists(filePath))
                        {
                            // Truncates the file to 0 bytes safely instead of deleting to bypass occasional thread handles locks
                            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                        }
                    });

                    LoadSelectedLog();
                }
                catch (Exception ex)
                {
                    _viewService.Alert($"Failed to erase file:\n{ex.Message}", "Error");
                }
            }
        }
    }
}