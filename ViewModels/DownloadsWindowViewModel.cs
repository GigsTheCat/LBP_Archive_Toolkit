using System.Diagnostics;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class DownloadsWindowViewModel : ViewModelBase
    {
        public ICommand DownloadBasicDbCommand { get; }
        public ICommand DownloadFastDbCommand { get; }
        public ICommand DownloadFullFastDbCommand { get; }
        public ICommand DownloadUltimateDbCommand { get; }

        public DownloadsWindowViewModel()
        {
            DownloadBasicDbCommand = new RelayCommand(_ => ExecuteDownload("https://archive.org/download/dry23db"));
            DownloadFastDbCommand = new RelayCommand(_ => ExecuteDownload("https://archive.org/download/fastdry"));
            DownloadFullFastDbCommand = new RelayCommand(_ => ExecuteDownload("https://archive.org/download/fullfastdry"));
            DownloadUltimateDbCommand = new RelayCommand(_ => ExecuteDownload("https://archive.org/download/ultimatefastdry"));
        }

        private void ExecuteDownload(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}