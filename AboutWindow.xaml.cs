using System.Windows;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            var viewModel = new AboutWindowViewModel();
            
            // Allow the ViewModel to close this window using the delegate
            viewModel.RequestClose += () => this.Close();
            
            DataContext = viewModel;
        }

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}