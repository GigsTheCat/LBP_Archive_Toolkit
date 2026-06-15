using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace LbpArchiveToolkit
{
    /// <summary>
    /// A blocking dialog that reports visual progress for background operations and manages task cancellation.
    /// </summary>
    public partial class ProgressWindow : Window
    {
        #region State & Properties

        public CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();

        #endregion

        #region Initialization & Lifecycle

        public ProgressWindow()
        {
            InitializeComponent();
            
            this.Closing += (s, e) => 
            { 
                if (!CancellationTokenSource.IsCancellationRequested) 
                {
                    CancellationTokenSource.Cancel(); 
                }
            };
        }

        #endregion

        #region Progress Management

        /// <summary>
        /// Updates the visual progress bar and text statuses. Handles dynamic theme coloring based on context.
        /// </summary>
        public void UpdateProgress(int current, int max, string mainMessage, string subMessage)
        {
            pbProgress.Maximum = max == 0 ? 1 : max;
            pbProgress.Value = current;
            
            txtStatus.Text = mainMessage;
            txtSubStatus.Text = subMessage;

            // Shift the sub-text to an alert color if the server forces a pause/timeout
            if (subMessage.Contains("Paused") || subMessage.Contains("Timeout"))
            {
                txtSubStatus.Foreground = (Brush)FindResource("LbpOrange");
            }
            else
            {
                txtSubStatus.Foreground = (Brush)FindResource("FgSecondary");
            }
        }

        #endregion

        #region UI Event Handlers

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            btnCancel.IsEnabled = false;
            btnCancel.Content = "CANCELLING...";
            txtStatus.Text = "Waiting for current download threads to exit...";
            
            CancellationTokenSource.Cancel();
        }

        private void TitleBar_Close_Click(object sender, RoutedEventArgs e)
        {
            BtnCancel_Click(sender, e);
        }

        #endregion
    }
}