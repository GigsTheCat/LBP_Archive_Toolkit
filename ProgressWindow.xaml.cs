using System;
using System.Threading;
using System.Windows;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit
{
    public partial class ProgressWindow : Window
    {
        private readonly ProgressWindowViewModel _viewModel;
        
        // Retained to preserve seamless API access for the extraction background task.
        public CancellationTokenSource CancellationTokenSource => _viewModel.CancellationTokenSource;

        public ProgressWindow()
        {
            InitializeComponent();
            _viewModel = new ProgressWindowViewModel();
            DataContext = _viewModel;

            this.Closing += (s, e) =>
            {
                if (!_viewModel.CancellationTokenSource.IsCancellationRequested)
                {
                    _viewModel.CancellationTokenSource.Cancel();
                }
            };
        }

        public void UpdateProgress(int current, int max, string mainMessage, string subMessage)
        {
            _viewModel.UpdateProgress(current, max, mainMessage, subMessage);
        }

        private void TitleBar_Close_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.CancelCommand.CanExecute(null))
            {
                _viewModel.CancelCommand.Execute(null);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _viewModel.Dispose();
        }
    }
}