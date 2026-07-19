using System;
using System.Threading;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class ProgressWindowViewModel : ViewModelBase
    {
        public CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();

        private int _progressMaximum = 1;
        public int ProgressMaximum { get => _progressMaximum; set => SetProperty(ref _progressMaximum, value); }

        private int _progressValue = 0;
        public int ProgressValue { get => _progressValue; set => SetProperty(ref _progressValue, value); }

        private string _statusText = "Preparing extraction...";
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        private string _subStatusText = "Initializing...";
        public string SubStatusText { get => _subStatusText; set => SetProperty(ref _subStatusText, value); }

        private bool _isErrorState = false;
        public bool IsErrorState { get => _isErrorState; set => SetProperty(ref _isErrorState, value); }

        private string _cancelButtonText = "CANCEL";
        public string CancelButtonText { get => _cancelButtonText; set => SetProperty(ref _cancelButtonText, value); }

        private bool _canCancel = true;
        public bool CanCancel { get => _canCancel; set => SetProperty(ref _canCancel, value); }

        public ICommand CancelCommand { get; }

        public ProgressWindowViewModel()
        {
            CancelCommand = new RelayCommand(_ => ExecuteCancel(), _ => CanCancel);
        }

        private void ExecuteCancel()
        {
            CanCancel = false;
            CancelButtonText = "CANCELLING...";
            StatusText = "Waiting for current download threads to exit...";
            CancellationTokenSource.Cancel();
        }

        public void UpdateProgress(int current, int max, string mainMessage, string subMessage)
        {
            ProgressMaximum = max == 0 ? 1 : max;
            ProgressValue = current;
            StatusText = mainMessage;
            SubStatusText = subMessage;

            IsErrorState = subMessage.Contains("Paused") || subMessage.Contains("Timeout");
        }

        public void Dispose()
        {
            CancellationTokenSource.Dispose();
        }
    }
}