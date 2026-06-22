using System.Windows;
using System.Windows.Input;
using System.IO;
using System.Windows.Media.Imaging;

namespace LbpArchiveToolkit
{
    public partial class EditInfoDialog : Window
    {
        public string LevelName => txtTitle.Text;
        public string Description => txtDescription.Text;
        public string? NewIconPath { get; private set; }

        public EditInfoDialog(string currentName, string currentDesc, string? currentIconPath)
        {
            InitializeComponent();
            txtTitle.Text = currentName;
            txtDescription.Text = currentDesc;
            UpdateTitleCount();
            UpdateDescCount();

            if (File.Exists(currentIconPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    using (var ms = new FileStream(currentIconPath, FileMode.Open, FileAccess.Read))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                    }
                    bmp.Freeze();
                    imgIcon.Source = bmp;
                }
                catch { }
            }
        }

        private void BtnChangeIcon_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
                Title = "Select New Icon"
            };

            if (dlg.ShowDialog() == true)
            {
                NewIconPath = dlg.FileName;
                try
                {
                    var bmp = new BitmapImage();
                    using (var ms = new FileStream(NewIconPath, FileMode.Open, FileAccess.Read))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                    }
                    bmp.Freeze();
                    imgIcon.Source = bmp;
                }
                catch
                {
                    CustomDialog.Show(this, "Failed to load the selected image.", "Error");
                    NewIconPath = null;
                }
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void TxtTitle_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateTitleCount();
        private void TxtDescription_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateDescCount();

        private void UpdateTitleCount()
        {
            if (txtTitleCount != null) txtTitleCount.Text = $"{txtTitle.Text.Length} / 100";
        }
        private void UpdateDescCount()
        {
            if (txtDescCount != null) txtDescCount.Text = $"{txtDescription.Text.Length} / 1000";
        }
    }
}