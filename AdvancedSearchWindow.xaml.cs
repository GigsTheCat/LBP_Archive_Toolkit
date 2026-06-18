using System.Windows;
using System.Windows.Controls;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit
{
    public partial class AdvancedSearchWindow : Window
    {
        public AdvancedSearchCriteria Criteria { get; private set; }

        public AdvancedSearchWindow(AdvancedSearchCriteria existingCriteria)
        {
            InitializeComponent();
            Criteria = existingCriteria;
            
            // Load existing numbers
            txtMinHearts.Text = Criteria.MinHearts.ToString();
            txtMinPlays.Text = Criteria.MinPlays.ToString();

            // Build dynamic checkboxes for labels
            foreach (var labelName in LabelParser.GetFriendlyNames())
            {
                var cb = new CheckBox
                {
                    Content = labelName,
                    Width = 140, // Keeps grid tidy
                    Margin = new Thickness(0, 5, 10, 5),
                    IsChecked = Criteria.RequiredLabels.Contains(labelName)
                };
                wpLabels.Children.Add(cb);
            }
        }

        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            txtMinHearts.Text = "0";
            txtMinPlays.Text = "0";
            
            foreach (var child in wpLabels.Children)
            {
                if (child is CheckBox cb) cb.IsChecked = false;
            }
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            int.TryParse(txtMinHearts.Text, out int hearts);
            int.TryParse(txtMinPlays.Text, out int plays);

            Criteria.MinHearts = hearts;
            Criteria.MinPlays = plays;
            Criteria.RequiredLabels.Clear();

            foreach (var child in wpLabels.Children)
            {
                if (child is CheckBox cb && cb.IsChecked == true && cb.Content != null)
                {
                    Criteria.RequiredLabels.Add(cb.Content.ToString()!);
                }
            }

            DialogResult = true;
            Close();
        }
    }
}