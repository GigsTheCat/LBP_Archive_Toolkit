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
            Process.Start(new ProcessStartInfo("https://archive.org/download/dry23db") { UseShellExecute = true });
            DialogResult = false; // Closes dialog after opening URL
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}