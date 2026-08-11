using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class DownloadsWindowViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;

        public ICommand DownloadBasicDbCommand { get; }
        public ICommand DownloadFastDbCommand { get; }
        public ICommand DownloadFullFastDbCommand { get; }
        public ICommand DownloadUltimateDbCommand { get; }

        public DownloadsWindowViewModel(IViewService viewService) : base(viewService)
        {
            _viewService = viewService;
            DownloadBasicDbCommand = new RelayCommand(_ => ExecuteDownload("https://archive.org/download/dry23db"));
            DownloadFastDbCommand = new RelayCommand(_ => ExecuteDownload("https://archive.org/download/fastdry"));
            DownloadFullFastDbCommand = new RelayCommand(_ => ExecuteDownload("https://archive.org/download/fullfastdry"));
            DownloadUltimateDbCommand = new RelayCommand(_ => ExecuteDownload("https://archive.org/download/ultimatefastdry"));
        }

        private void ExecuteDownload(string url)
        {
            _viewService.OpenUrl(url);
        }
    }
}