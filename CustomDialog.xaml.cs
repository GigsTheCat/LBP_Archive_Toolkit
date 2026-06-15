using System.Windows;
using System.Windows.Input;

namespace LbpArchiveToolkit
{
    public partial class CustomDialog : Window
    {
        public CustomDialog(string message, string title, bool isYesNo)
        {
            InitializeComponent();
            txtTitle.Text = title;
            txtMessage.Text = message;

            if (isYesNo)
            {
                btnOk.Visibility = Visibility.Collapsed;
                btnYes.Visibility = Visibility.Visible;
                btnNo.Visibility = Visibility.Visible;
            }
            else
            {
                btnOk.Visibility = Visibility.Visible;
                btnYes.Visibility = Visibility.Collapsed;
                btnNo.Visibility = Visibility.Collapsed;
            }
        }

        // Makes the custom borderless window draggable
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void BtnYes_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void BtnNo_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        // Easy static helper to replace MessageBox.Show
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