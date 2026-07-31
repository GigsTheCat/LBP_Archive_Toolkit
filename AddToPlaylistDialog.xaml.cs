using System.Windows;
using System.Windows.Input;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit
{
    public partial class AddToPlaylistDialog : Window
    {
        public AddToPlaylistDialog(LevelItem level)
        {
            InitializeComponent();
            var vm = new AddToPlaylistDialogViewModel(level);
            vm.RequestClose += (result) => { DialogResult = result; Close(); };
            DataContext = vm;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}