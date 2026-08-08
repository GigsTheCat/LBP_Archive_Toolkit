using System.Windows;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit
{
    public partial class DownloadsWindow : Window
    {
        public DownloadsWindow()
        {
            InitializeComponent();
            var viewService = (ViewModels.IViewService)Application.Current.MainWindow;
            DataContext = new DownloadsWindowViewModel(viewService);
            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
        }

        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}