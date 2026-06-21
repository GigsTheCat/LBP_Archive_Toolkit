using System;
using System.Collections.Generic;
using System.Windows;

namespace LbpArchiveToolkit.Themes
{
    public static class ThemeManager
    {
        // Expose safelisted themes and their display names so the UI can build its lists dynamically
        public static readonly IReadOnlyDictionary<string, string> AvailableThemes = new Dictionary<string, string>
        {
            { "DefaultTheme", "Default Theme" },
            { "CraftTheme", "Craft Theme" }
        };

        public static void ApplyTheme(string themeName)
        {
            // Security: Prevent XAML injection by explicitly checking against our allowed themes
            if (string.IsNullOrEmpty(themeName) || !AvailableThemes.ContainsKey(themeName))
            {
                themeName = "DefaultTheme";
            }

            var app = Application.Current;
            var dict = new ResourceDictionary
            {
                Source = new Uri($"Themes/{themeName}.xaml", UriKind.Relative)
            };
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(dict);
        }
    }
}