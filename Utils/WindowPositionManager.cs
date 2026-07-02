using System.Windows;
using LbpArchiveToolkit.Configuration;

namespace LbpArchiveToolkit.Utils
{
    public static class WindowPositionManager
    {
        public static void RestorePosition(Window window)
        {
            bool hasSavedLocation = ConfigManager.WindowLeft != -1 && ConfigManager.WindowTop != -1;
            if (ConfigManager.WindowWidth > 0 && ConfigManager.WindowHeight > 0 && hasSavedLocation)
            {
                window.Width = ConfigManager.WindowWidth;
                window.Height = ConfigManager.WindowHeight;

                double virtualLeft = SystemParameters.VirtualScreenLeft;
                double virtualTop = SystemParameters.VirtualScreenTop;
                double virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
                double virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

                bool isOffScreen =
                    ConfigManager.WindowLeft >= virtualRight || ConfigManager.WindowTop >= virtualBottom ||
                    (ConfigManager.WindowLeft + ConfigManager.WindowWidth) <= virtualLeft ||
                    (ConfigManager.WindowTop + ConfigManager.WindowHeight) <= virtualTop;

                if (!isOffScreen)
                {
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Left = ConfigManager.WindowLeft;
                    window.Top = ConfigManager.WindowTop;
                }
            }
        }

        public static void SavePosition(Window window)
        {
            ConfigManager.IsMaximized = (window.WindowState == WindowState.Maximized);
            if (window.WindowState == WindowState.Normal)
            {
                ConfigManager.WindowWidth = window.Width;
                ConfigManager.WindowHeight = window.Height;
                ConfigManager.WindowLeft = window.Left;
                ConfigManager.WindowTop = window.Top;
            }
            else
            {
                ConfigManager.WindowWidth = window.RestoreBounds.Width;
                ConfigManager.WindowHeight = window.RestoreBounds.Height;
                ConfigManager.WindowLeft = window.RestoreBounds.Left;
                ConfigManager.WindowTop = window.RestoreBounds.Top;
            }
        }
    }
}