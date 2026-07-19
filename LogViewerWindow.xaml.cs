using LbpArchiveToolkit.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace LbpArchiveToolkit
{
    public partial class LogViewerWindow : Window
    {
        public LogViewerWindow()
        {
            InitializeComponent();
            
            // Acquire App IViewService to inject into the viewmodel
            var viewService = (IViewService)Application.Current.MainWindow;
            DataContext = new LogViewerWindowViewModel(viewService);

            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
        }

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // This stays in code-behind because view-scroll manipulation breaks MVVM pattern if placed in ViewModel
        private void TxtLogDisplay_TextChanged(object sender, TextChangedEventArgs e)
        {
            txtLogDisplay.ScrollToEnd();
        }
    }
}