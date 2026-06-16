using System.Diagnostics;
using System.Reflection;
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

            var attr = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attr != null)
            {
                string versionStr = attr.InformationalVersion;
                int plusIndex = versionStr.IndexOf('+');
                if (plusIndex > 0)
                {
                    versionStr = versionStr.Substring(0, plusIndex);
                }
                txtVersion.Text = $"Version {versionStr}";
            }
            else
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                txtVersion.Text = $"Version {version?.ToString()}";
            }
        }

        
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}