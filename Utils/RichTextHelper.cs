using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Services;

namespace LbpArchiveToolkit.Utils
{
    public static partial class RichTextHelper
    {
        [GeneratedRegex(@"(\@[a-zA-Z0-9_-]+)")]
        private static partial Regex MentionRegex();

        public static void SetDescriptionRichText(RichTextBox txtDescription, string? text, Action<string> onMentionClick)
        {
            txtDescription.IsDocumentEnabled = true;
            txtDescription.Document.Blocks.Clear();
            if (string.IsNullOrEmpty(text)) return;

            int lastIndex = 0;
            FlowDocument doc = txtDescription.Document;
            Paragraph para = new Paragraph();

            foreach (var match in MentionRegex().EnumerateMatches(text))
            {
                if (match.Index > lastIndex)
                {
                    para.Inlines.Add(new Run(text.Substring(lastIndex, match.Index - lastIndex)));
                }

                string mentionStr = text.Substring(match.Index, match.Length);
                Hyperlink link = new Hyperlink(new Run(mentionStr))
                {
                    Foreground = Brushes.LightBlue,
                    Cursor = Cursors.Hand
                };

                link.Click += (s, e) =>
                {
                    onMentionClick(mentionStr.Substring(1));
                    e.Handled = true;
                };
                CreatorPreviewBehavior.SetCreatorName(link, mentionStr.Substring(1));
                para.Inlines.Add(link);
                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
            {
                para.Inlines.Add(new Run(text.Substring(lastIndex)));
            }

            doc.Blocks.Add(para);
        }
    }

    public static class CreatorPreviewBehavior
    {
        public static readonly DependencyProperty CreatorNameProperty =
            DependencyProperty.RegisterAttached("CreatorName", typeof(string), typeof(CreatorPreviewBehavior),
                new PropertyMetadata(null, OnCreatorNameChanged));

        public static string GetCreatorName(DependencyObject obj) => (string)obj.GetValue(CreatorNameProperty);
        public static void SetCreatorName(DependencyObject obj, string value) => obj.SetValue(CreatorNameProperty, value);

        private static void OnCreatorNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element)
            {
                if (e.NewValue is string creatorName && !string.IsNullOrWhiteSpace(creatorName))
                {
                    element.ToolTipOpening -= OnToolTipOpening;
                    element.ToolTipOpening += OnToolTipOpening;

                    if (element.ToolTip == null)
                    {
                        element.ToolTip = new ToolTip { Content = "Loading..." };
                    }
                }
                else
                {
                    element.ToolTipOpening -= OnToolTipOpening;
                    element.ToolTip = null;
                }
            }
            else if (d is FrameworkContentElement fce)
            {
                if (e.NewValue is string creatorName && !string.IsNullOrWhiteSpace(creatorName))
                {
                    fce.ToolTipOpening -= OnToolTipOpening;
                    fce.ToolTipOpening += OnToolTipOpening;

                    if (fce.ToolTip == null)
                    {
                        fce.ToolTip = new ToolTip { Content = "Loading..." };
                    }
                }
                else
                {
                    fce.ToolTipOpening -= OnToolTipOpening;
                    fce.ToolTip = null;
                }
            }
        }

        private static async void OnToolTipOpening(object sender, ToolTipEventArgs e)
        {
            string? creatorName = null;
            ToolTip? toolTip = null;

            if (sender is FrameworkElement element)
            {
                creatorName = GetCreatorName(element);
                toolTip = element.ToolTip as ToolTip;
                if (toolTip == null)
                {
                    toolTip = new ToolTip();
                    element.ToolTip = toolTip;
                }
            }
            else if (sender is FrameworkContentElement fce)
            {
                creatorName = GetCreatorName(fce);
                toolTip = fce.ToolTip as ToolTip;
                if (toolTip == null)
                {
                    toolTip = new ToolTip();
                    fce.ToolTip = toolTip;
                }
            }

            if (string.IsNullOrWhiteSpace(creatorName) || toolTip == null) return;
            if (toolTip.Tag as string == creatorName) return; // Prevent unnecessary reloading

            var loadingText = new TextBlock { Text = "Loading...", Foreground = (Brush)Application.Current.FindResource("FgPrimary") };
            toolTip.Content = loadingText;
            toolTip.Tag = creatorName; 

            try
            {
                var dbService = new DatabaseService(ConfigManager.DatabasePath);
                var users = await dbService.SearchUsersAsync(creatorName, true, "1");

                if (users.Count > 0)
                {
                    var user = users[0];
                    var grid = new Grid { Margin = new Thickness(5) };
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var iconRect = new Rectangle
                    {
                        Width = 64,
                        Height = 64,
                        RadiusX = 8,
                        RadiusY = 8,
                        Fill = new SolidColorBrush(Color.FromRgb(25, 19, 43)),
                        Stroke = (Brush)Application.Current.FindResource("LbpOrange"),
                        StrokeThickness = 2,
                        Margin = new Thickness(0, 0, 15, 0)
                    };

                    grid.Children.Add(iconRect);

                    var stackPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(stackPanel, 1);

                    var nameBlock = new TextBlock
                    {
                        Text = user.NpHandle,
                        Foreground = (Brush)Application.Current.FindResource("LbpCyan"),
                        FontWeight = FontWeights.Bold,
                        FontSize = 16,
                        Margin = new Thickness(0, 0, 0, 5)
                    };

                    var levelsBlock = new TextBlock
                    {
                        Text = $"Total Levels: {user.TotalLevels}",
                        Foreground = (Brush)Application.Current.FindResource("FgPrimary"),
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold
                    };

                    stackPanel.Children.Add(nameBlock);
                    stackPanel.Children.Add(levelsBlock);
                    grid.Children.Add(stackPanel);

                    toolTip.Content = grid;

                    var brush = await IconLoaderService.LoadIconBrushAsync(user.IconHash, MainWindow.SharedHttpClient, CancellationToken.None);
                    if (brush != null)
                    {
                        iconRect.Fill = brush;
                    }
                }
                else
                {
                    toolTip.Content = new TextBlock { Text = "Creator not found in database.", Foreground = (Brush)Application.Current.FindResource("FgSecondary") };
                }
            }
            catch
            {
                toolTip.Content = new TextBlock { Text = "Error loading creator info.", Foreground = (Brush)Application.Current.FindResource("FgSecondary") };
            }
        }
    }
}