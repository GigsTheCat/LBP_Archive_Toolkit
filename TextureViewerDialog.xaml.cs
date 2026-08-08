using System.Windows;
using System.Windows.Input;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit
{
    public partial class TextureViewerDialog : Window
    {
        public TextureViewerDialog(string backupPath, string levelName)
        {
            InitializeComponent();
            
            var viewService = (IViewService)Application.Current.MainWindow;
            var viewModel = new TextureViewerDialogViewModel(viewService, backupPath, levelName);
            
            viewModel.RequestClose += () => Close();
            DataContext = viewModel;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}