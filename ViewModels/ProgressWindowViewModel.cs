using System;
using System.Threading;
using System.Windows.Input;

namespace LbpArchiveToolkit.ViewModels
{
    public class ProgressWindowViewModel : ViewModelBase
    {
        public CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();

        public int ProgressMaximum { get; set => SetProperty(ref field, value); } = 1;
        public int ProgressValue { get; set => SetProperty(ref field, value); } = 0;
        public string StatusText { get; set => SetProperty(ref field, value); } = "Preparing extraction...";
        public string SubStatusText { get; set => SetProperty(ref field, value); } = "Initializing...";
        public bool IsErrorState { get; set => SetProperty(ref field, value); } = false;
        public string CancelButtonText { get; set => SetProperty(ref field, value); } = "CANCEL";
        public bool CanCancel { get; set => SetProperty(ref field, value); } = true;

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