using System.Diagnostics;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class DownloadsWindowViewModel : ViewModelBase
    {
        public ICommand DownloadBasicDbCommand { get; }
        public ICommand DownloadFastDbCommand { get; }
        public ICommand DownloadFullFastDbCommand { get; }

        public DownloadsWindowViewModel()
        {
            DownloadBasicDbCommand = new RelayCommand(_ => ExecuteDownload("https://archive.org/download/dry23db"));
            DownloadFastDbCommand = new RelayCommand(_ => ExecuteDownload("https://archive.org/download/fastdry"));
            DownloadFullFastDbCommand = new RelayCommand(_ => ExecuteDownload("https://archive.org/download/fullfastdry"));
        }

        private void ExecuteDownload(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}