using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace LbpArchiveToolkit
{
    public partial class MissingDatabaseDialog : Window
    {
        public MissingDatabaseDialog()
        {
            InitializeComponent();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true; // Signals parent to open the Settings window
        }

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            this.Opacity = 0;

            var downloadsWin = new DownloadsWindow { Owner = this.Owner ?? this };
            downloadsWin.ShowDialog();

            DialogResult = true; // Closes dialog completely and signals parent to open Settings
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}