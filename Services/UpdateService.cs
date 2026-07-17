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
                string? body = json?["body"]?.ToString(); // Extract the release notes

                if (!string.IsNullOrEmpty(tag))
                {
                    string versionStr = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
                    if (Version.TryParse(versionStr, out Version? latestVersion))
                    {
                        var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                        if (currentVersion != null && latestVersion > currentVersion)
                        {
                            if (!ownerWindow.IsVisible) return;

                            // Format the update message to include the patch notes
                            string message = $"A new version ({tag}) of LBP Archive Toolkit is available.\n\n";
                            
                            if (!string.IsNullOrWhiteSpace(body))
                            {
                                // Strip \r\n to standard \n for WPF consistency, and append the notes
                                string patchNotes = body.Replace("\r\n", "\n").Trim();
                                message += $"Patch Notes:\n{patchNotes}\n\n";
                            }
                            
                            message += "Would you like to download it now?";

                            // The CustomDialog already has a ScrollViewer, so long patch notes will scroll naturally
                            bool update = CustomDialog.Show(ownerWindow, message, "Update Available", isYesNo: true);
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