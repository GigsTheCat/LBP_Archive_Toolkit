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
            
            txtMinHearts.Text = Criteria.MinHearts.ToString();
            txtMinPlays.Text = Criteria.MinPlays.ToString();

            // LBP2 and LBP3 Labels
            foreach (var labelName in LabelParser.GetFriendlyNames())
            {
                var cb = new CheckBox
                {
                    Content = labelName,
                    Width = 140,
                    Margin = new Thickness(0, 5, 10, 5),
                    IsChecked = Criteria.RequiredLabels.Contains(labelName)
                };
                
                if (LabelParser.IsLbp2Label(labelName)) wpLbp2Labels.Children.Add(cb);
                else wpLbp3Labels.Children.Add(cb);
            }

            // LBP1 Tags
            foreach (var tagName in TagParser.GetNames())
            {
                var cb = new CheckBox
                {
                    Content = tagName,
                    Width = 140,
                    Margin = new Thickness(0, 5, 10, 5),
                    IsChecked = Criteria.RequiredTags.Contains(tagName)
                };
                wpLbp1Tags.Children.Add(cb);
            }
        }

        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            txtMinHearts.Text = "0";
            txtMinPlays.Text = "0";
            
            foreach (var child in wpLbp2Labels.Children) if (child is CheckBox cb) cb.IsChecked = false;
            foreach (var child in wpLbp3Labels.Children) if (child is CheckBox cb) cb.IsChecked = false;
            foreach (var child in wpLbp1Tags.Children) if (child is CheckBox cb) cb.IsChecked = false;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            int.TryParse(txtMinHearts.Text, out int hearts);
            int.TryParse(txtMinPlays.Text, out int plays);

            Criteria.MinHearts = hearts;
            Criteria.MinPlays = plays;
            Criteria.RequiredLabels.Clear();
            Criteria.RequiredTags.Clear();

            foreach (var child in wpLbp2Labels.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredLabels.Add(cb.Content.ToString()!);
                
            foreach (var child in wpLbp3Labels.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredLabels.Add(cb.Content.ToString()!);

            foreach (var child in wpLbp1Tags.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredTags.Add(cb.Content.ToString()!);

            DialogResult = true;
            Close();
        }
    }
}