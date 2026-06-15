using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace LbpArchiveToolkit
{
    public partial class AboutWindow : Window
    {
        private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        public AboutWindow()
        {
            InitializeComponent();
        }

        
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}