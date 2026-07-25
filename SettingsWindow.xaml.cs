using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.ViewModels;
using System;
using System.Windows;

namespace LbpArchiveToolkit
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            
            // Acquire IViewService to inject into the viewmodel
            var viewService = (IViewService)Application.Current.MainWindow;
            var viewModel = new SettingsWindowViewModel(viewService);

            // Handle ViewModel dialog closure requests
            viewModel.RequestClose += (result) =>
            {
                DialogResult = result;
                Close();
            };

            DataContext = viewModel;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DialogResult != true)
            {
                // Revert to original theme if closed without saving
                LbpArchiveToolkit.Themes.ThemeManager.ApplyTheme(ConfigManager.Theme);
            }
            base.OnClosed(e);
        }
        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}