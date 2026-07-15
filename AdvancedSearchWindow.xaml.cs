using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Utils;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

            Style tagStyle = (Style)FindResource("TagCheckBox");

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
                    Margin = new Thickness(2, 2, 2, 2), // Condensed layout margins
                    IsChecked = Criteria.RequiredLabels.Contains(tag),
                    Style = tagStyle,
                    LayoutTransform = new RotateTransform(GetDeterministicTilt(tag))
                };

                bool isLbp2 = LabelParser.IsLbp2LabelByTag(tag);
                string category = LabelParser.GetLabelCategory(tag);

                if (isLbp2)
                {
                    if (category == "Experience") wpLbp2ExperienceLabels.Children.Add(cb);
                    else if (category == "Type") wpLbp2TypeLabels.Children.Add(cb);
                    else wpLbp2ContentLabels.Children.Add(cb);
                }
                else
                {
                    if (category == "Experience") wpLbp3ExperienceLabels.Children.Add(cb);
                    else if (category == "Type") wpLbp3TypeLabels.Children.Add(cb);
                    else wpLbp3ContentLabels.Children.Add(cb);
                }
            }

            // LBP1 Tags
            foreach (var tagName in TagParser.GetNames())
            {
                var cb = new CheckBox
                {
                    Content = tagName,
                    Tag = tagName, // Use Tag here as well for consistency
                    Margin = new Thickness(2, 2, 2, 2),
                    IsChecked = Criteria.RequiredTags.Contains(tagName),
                    Style = tagStyle,
                    LayoutTransform = new RotateTransform(GetDeterministicTilt(tagName))
                };
                wpLbp1Tags.Children.Add(cb);
            }
        }

        /// <summary>
        /// Computes a stable, deterministic visual skew degree mapped between [-3.0, 3.0]
        /// using the FNV-1a non-cryptographic hashing algorithm.
        /// </summary>
        private double GetDeterministicTilt(string str)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in str)
                {
                    hash = (hash ^ c) * 16777619;
                }
                double val = (hash % 10000) / 10000.0;
                return val * 6.0 - 3.0; // Maps precisely to a [-3.0, 3.0] angle range
            }
        }

        private void TitleBar_Close_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            txtMinHearts.Text = "0";
            txtMinPlays.Text = "0";
            chkTeamPick.IsChecked = false;

            foreach (var child in wpLbp2ExperienceLabels.Children) if (child is CheckBox cb) cb.IsChecked = false;
            foreach (var child in wpLbp2TypeLabels.Children) if (child is CheckBox cb) cb.IsChecked = false;
            foreach (var child in wpLbp2ContentLabels.Children) if (child is CheckBox cb) cb.IsChecked = false;
            foreach (var child in wpLbp3ExperienceLabels.Children) if (child is CheckBox cb) cb.IsChecked = false;
            foreach (var child in wpLbp3TypeLabels.Children) if (child is CheckBox cb) cb.IsChecked = false;
            foreach (var child in wpLbp3ContentLabels.Children) if (child is CheckBox cb) cb.IsChecked = false;
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

            foreach (var child in wpLbp2ExperienceLabels.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredLabels.Add(cb.Tag.ToString()!);

            foreach (var child in wpLbp2TypeLabels.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredLabels.Add(cb.Tag.ToString()!);

            foreach (var child in wpLbp2ContentLabels.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredLabels.Add(cb.Tag.ToString()!);

            foreach (var child in wpLbp3ExperienceLabels.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredLabels.Add(cb.Tag.ToString()!);

            foreach (var child in wpLbp3TypeLabels.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredLabels.Add(cb.Tag.ToString()!);

            foreach (var child in wpLbp3ContentLabels.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredLabels.Add(cb.Tag.ToString()!);

            foreach (var child in wpLbp1Tags.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredTags.Add(cb.Tag.ToString()!);
        }
    }
}