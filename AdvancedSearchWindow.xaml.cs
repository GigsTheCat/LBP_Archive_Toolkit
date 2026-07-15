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
            var allTags = LabelParser.GetTags();
            var allFriendly = LabelParser.GetFriendlyNames();

            void CreateCheckbox(string tag, string friendlyName, WrapPanel panel)
            {
                var cb = new CheckBox
                {
                    Content = friendlyName,
                    Tag = tag, // Store the raw internal tag behind the scenes
                    Margin = new Thickness(2, 2, 2, 2), // Condensed layout margins
                    IsChecked = Criteria.RequiredLabels.Contains(tag),
                    Style = tagStyle,
                    LayoutTransform = new RotateTransform(GetDeterministicTilt(tag))
                };
                panel.Children.Add(cb);
            }

            // Dynamically categorize all labels optimally
            for (int i = 0; i < allTags.Count; i++)
            {
                string tag = allTags[i];
                string friendly = allFriendly[i];
                string category = LabelParser.GetLabelCategory(tag);
                bool isLbp2 = LabelParser.IsLbp2LabelByTag(tag);

                if (isLbp2)
                {
                    if (category == "Experience") CreateCheckbox(tag, friendly, wpLbp2ExperienceLabels);
                    else if (category == "Type") CreateCheckbox(tag, friendly, wpLbp2TypeLabels);
                    else CreateCheckbox(tag, friendly, wpLbp2ContentLabels);
                }
                else
                {
                    if (category == "Character") CreateCheckbox(tag, friendly, wpLbp3CharactersLabels);
                    else if (category == "Experience") CreateCheckbox(tag, friendly, wpLbp3ExperienceLabels);
                    else if (category == "Type") CreateCheckbox(tag, friendly, wpLbp3TypeLabels);
                    else CreateCheckbox(tag, friendly, wpLbp3ContentLabels);
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
            foreach (var child in wpLbp3CharactersLabels.Children) if (child is CheckBox cb) cb.IsChecked = false;
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

            foreach (var child in wpLbp3CharactersLabels.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredLabels.Add(cb.Tag.ToString()!);

            foreach (var child in wpLbp1Tags.Children)
                if (child is CheckBox cb && cb.IsChecked == true) Criteria.RequiredTags.Add(cb.Tag.ToString()!);
        }
    }
}