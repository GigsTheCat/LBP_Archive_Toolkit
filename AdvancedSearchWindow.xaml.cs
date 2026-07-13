using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Utils;
using System.Windows;
using System.Windows.Controls;

namespace LbpArchiveToolkit
{
    public partial class AdvancedSearchWindow : Window
    {
        public AdvancedSearchCriteria Criteria { get; private set; }
        public bool ShouldSearch { get; private set; }

        public AdvancedSearchWindow(AdvancedSearchCriteria existingCriteria)
        {
            InitializeComponent();
            Criteria = existingCriteria;

            txtMinHearts.Text = Criteria.MinHearts.ToString();
            txtMinPlays.Text = Criteria.MinPlays.ToString();
            chkTeamPick.IsChecked = Criteria.IsTeamPick;

            // LBP2 and LBP3 Labels
            var labelTags = LabelParser.GetTags();
            var friendlyNames = LabelParser.GetFriendlyNames();
            for (int i = 0; i < labelTags.Count; i++)
            {
                string tag = labelTags[i];
                string friendly = friendlyNames[i];

                var cb = new CheckBox
                {
                    Content = friendly,
                    Tag = tag, // Store the raw internal tag behind the scenes
                    Width = 165,
                    Margin = new Thickness(0, 5, 10, 5),
                    IsChecked = Criteria.RequiredLabels.Contains(tag)
                };

                if (LabelParser.IsLbp2LabelByTag(tag)) wpLbp2Labels.Children.Add(cb);
                else wpLbp3Labels.Children.Add(cb);
            }

            // LBP1 Tags
            foreach (var tagName in TagParser.GetNames())
            {
                var cb = new CheckBox
                {
                    Content = tagName,
                    Tag = tagName, // Use Tag here as well for consistency
                    Width = 165,
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
            chkTeamPick.IsChecked = false;

            foreach (var child in wpLbp2Labels.Children) if (child is CheckBox cb) cb.IsChecked = false;
            foreach (var child in wpLbp3Labels.Children) if (child is CheckBox cb) cb.IsChecked = false;
            foreach (var child in wpLbp1Tags.Children) if (child is CheckBox cb) cb.IsChecked = false;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ApplyCriteria();
            DialogResult = true;
            Close();
        }

        private void BtnApplyAndSearch_Click(object sender, RoutedEventArgs e)
        {
            ApplyCriteria();
            ShouldSearch = true;
            DialogResult = true;
            Close();
        }

        private void ApplyCriteria()
        {
            int.TryParse(txtMinHearts.Text, out int hearts);
            int.TryParse(txtMinPlays.Text, out int plays);

            Criteria.MinHearts = hearts;
            Criteria.MinPlays = plays;
            Criteria.IsTeamPick = chkTeamPick.IsChecked == true;
            Criteria.RequiredLabels.Clear();
            Criteria.RequiredTags.Clear();

            foreach (var child in wpLbp2Labels.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredLabels.Add(cb.Tag.ToString()!);

            foreach (var child in wpLbp3Labels.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredLabels.Add(cb.Tag.ToString()!);

            foreach (var child in wpLbp1Tags.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredTags.Add(cb.Tag.ToString()!);
        }
    }
}