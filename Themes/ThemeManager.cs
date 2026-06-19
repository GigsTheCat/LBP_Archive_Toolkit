using System;
using System.Windows;

namespace LbpArchiveToolkit.Themes
{
    public static class ThemeManager
    {
        public static void ApplyTheme(string themeName)
        {
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