using System;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class MissingDatabaseDialogViewModel : ViewModelBase
    {
        private readonly IViewService _viewService;

        public ICommand SettingsCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand CloseCommand { get; }

        public Action<bool>? RequestClose { get; set; }
        public Action? RequestHide { get; set; }

        public MissingDatabaseDialogViewModel(IViewService viewService) : base(viewService)
        {
            _viewService = viewService;

            SettingsCommand = new RelayCommand(_ => RequestClose?.Invoke(true));
            DownloadCommand = new RelayCommand(_ => ExecuteDownload());
            CloseCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
        }

        private void ExecuteDownload()
        {
            RequestHide?.Invoke();
            _viewService.OpenDownloads();
            RequestClose?.Invoke(true); // Signals parent to finish evaluating
        }
    }
}