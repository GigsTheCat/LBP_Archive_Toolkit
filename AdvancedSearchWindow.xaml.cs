using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.ViewModels;
using System.Windows;

namespace LbpArchiveToolkit
{
    public partial class AdvancedSearchWindow : Window
    {
        // Public properties retained to ensure compatibility with MainWindowViewModel calls
        public AdvancedSearchCriteria Criteria { get; private set; }
        public bool ShouldSearch { get; private set; }

        public AdvancedSearchWindow(AdvancedSearchCriteria existingCriteria)
        {
            InitializeComponent();
            
            // Set default in case the window is closed without pressing Apply
            Criteria = existingCriteria; 

            var viewModel = new AdvancedSearchWindowViewModel(existingCriteria);

            // Close callback executed when Apply or Apply&Search are pressed
            viewModel.RequestClose += (newCriteria, shouldSearch) =>
            {
                Criteria = newCriteria;
                ShouldSearch = shouldSearch;
                DialogResult = true;
                Close();
            };

            DataContext = viewModel;
        }

        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}