using System.Windows;
using System.Windows.Input;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit
{
    public partial class CustomDialog : Window
    {
        private readonly CustomDialogViewModel _viewModel;

        public bool IsCheckboxChecked => _viewModel.IsCheckboxChecked;

        public string InputText => _viewModel.InputText;

        public CustomDialog(string message, string title, bool isYesNo, string? checkboxText = null, bool isInput = false, string defaultInput = "")
        {
            InitializeComponent();
            var viewService = Application.Current.MainWindow as IViewService;
            _viewModel = new CustomDialogViewModel(viewService, message, title, isYesNo, checkboxText, isInput, defaultInput);
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

        public static bool ShowWithCheckbox(Window owner, string message, string title, string checkboxText, out bool isChecked, bool isYesNo = false)
        {
            var dialog = new CustomDialog(message, title, isYesNo, checkboxText)
            {
                Owner = owner
            };
            bool result = dialog.ShowDialog() == true;
            isChecked = dialog.IsCheckboxChecked;
            return result;
        }

        public static bool ShowInput(Window owner, string message, string title, string defaultText, out string inputText)
        {
            var dialog = new CustomDialog(message, title, isYesNo: true, checkboxText: null, isInput: true, defaultInput: defaultText)
            {
                Owner = owner
            };
            bool result = dialog.ShowDialog() == true;
            inputText = dialog.InputText;
            return result;
        }
    }
}