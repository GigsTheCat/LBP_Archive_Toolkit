using LbpArchiveToolkit.ViewModels;
using System.Windows;

namespace LbpArchiveToolkit
{
    public partial class HeartedCreatorsWindow : Window
    {
        public HeartedCreatorsWindow()
        {
            InitializeComponent();

            // Retrieve the application's IViewService to inject into the ViewModel 
            // (Implemented structurally in MainWindow and required for UI prompts)
            var viewService = (IViewService)Application.Current.MainWindow;
            
            var viewModel = new HeartedCreatorsWindowViewModel(viewService);
            
            // Allow the ViewModel to close this window using the delegate
            viewModel.RequestClose += () => this.Close();
            DataContext = viewModel;

            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
        }

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) 
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}