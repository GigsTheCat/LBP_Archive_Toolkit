using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using LbpArchiveToolkit.Configuration;

namespace LbpArchiveToolkit.Services
{
    public static class UpdateService
    {
        public static async Task CheckForUpdatesAsync(Window ownerWindow, HttpClient httpClient)
        {
            if ((DateTime.Now - ConfigManager.LastUpdateCheck).TotalHours < 12) return;

            try
            {
                ConfigManager.LastUpdateCheck = DateTime.Now;
                string url = "https://api.github.com/repos/GigsTheCat/LBP_Archive_Toolkit/releases/latest";
                var response = await httpClient.GetStringAsync(url);
                var json = JsonNode.Parse(response);
                string? tag = json?["tag_name"]?.ToString();

                if (!string.IsNullOrEmpty(tag))
                {
                    string versionStr = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
                    if (Version.TryParse(versionStr, out Version? latestVersion))
                    {
                        var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                        if (currentVersion != null && latestVersion > currentVersion)
                        {
                            if (!ownerWindow.IsVisible) return;

                            bool update = CustomDialog.Show(ownerWindow, "A new version of LBP Archive Toolkit is available.\n\nWould you like to download it now?", "Update Available", isYesNo: true);
                            if (update)
                            {
                                Process.Start(new ProcessStartInfo("https://github.com/GigsTheCat/LBP_Archive_Toolkit/releases") { UseShellExecute = true });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log("UpdateService.CheckForUpdatesAsync", ex);
            }
        }
    }
}