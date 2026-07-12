using System.Diagnostics;
using System.Windows;

namespace LbpArchiveToolkit
{
    public partial class DownloadsWindow : Window
    {
        public DownloadsWindow()
        {
            InitializeComponent();
            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
        }

        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnDownloadBasicDb_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://archive.org/download/dry23db") { UseShellExecute = true });
        }

        private void BtnDownloadFastDb_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://archive.org/download/fastdry") { UseShellExecute = true });
        }

        private void BtnDownloadFullFastDb_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://archive.org/download/fullfastdry") { UseShellExecute = true });
        }
    }
}