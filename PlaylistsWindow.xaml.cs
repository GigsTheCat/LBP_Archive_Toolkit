using LbpArchiveToolkit.ViewModels;
using System.Windows;

namespace LbpArchiveToolkit
{
    public partial class PlaylistsWindow : Window
    {
        public PlaylistsWindow()
        {
            InitializeComponent();

            var viewService = (IViewService)Application.Current.MainWindow;
            DataContext = new PlaylistsWindowViewModel(viewService);

            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
        }

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}