using System.Windows;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit
{
    public partial class DownloadsWindow : Window
    {
        public DownloadsWindow()
        {
            InitializeComponent();
            DataContext = new DownloadsWindowViewModel();
            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
        }

        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}