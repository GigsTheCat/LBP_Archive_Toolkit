using System.Windows;
using System.Windows.Input;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit
{
    public partial class CustomDialog : Window
    {
        private readonly CustomDialogViewModel _viewModel;

        public CustomDialog(string message, string title, bool isYesNo)
        {
            InitializeComponent();
            _viewModel = new CustomDialogViewModel(message, title, isYesNo);
            _viewModel.RequestClose += (result) => { DialogResult = result; Close(); };
            DataContext = _viewModel;

            if (!string.IsNullOrEmpty(message))
            {
                txtMessage.Text = message;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        public static bool Show(Window owner, string message, string title, bool isYesNo = false)
        {
            var dialog = new CustomDialog(message, title, isYesNo)
            {
                Owner = owner
            };
            return dialog.ShowDialog() == true;
        }
    }
}