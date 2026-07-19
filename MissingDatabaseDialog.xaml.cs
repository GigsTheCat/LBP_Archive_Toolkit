using System.Windows;
using System.Windows.Input;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit
{
    public partial class MissingDatabaseDialog : Window
    {
        public MissingDatabaseDialog()
        {
            InitializeComponent();
            var viewService = (IViewService)Application.Current.MainWindow;
            var vm = new MissingDatabaseDialogViewModel(viewService);
            
            vm.RequestClose += (result) => { DialogResult = result; Close(); };
            vm.RequestHide += () => this.Opacity = 0;
            
            DataContext = vm;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}