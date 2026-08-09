using LbpArchiveToolkit.ViewModels;
using System.Windows;

namespace LbpArchiveToolkit
{
    public partial class BackupManagerWindow : Window
    {
        public BackupManagerWindow()
        {
            InitializeComponent();

            // Retrieve the application's IViewService to inject into the ViewModel 
            // (Implemented structurally in MainWindow and required for UI prompts)
            var viewService = (IViewService)Application.Current.MainWindow;
            
            DataContext = new BackupManagerWindowViewModel(viewService);

            LbpArchiveToolkit.Utils.BorderlessWindowFix.Apply(this);
        }

        private void LvBackups_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (DataContext is BackupManagerWindowViewModel vm)
            {
                vm.InvalidateCommands();
            }
        }

        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) 
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}