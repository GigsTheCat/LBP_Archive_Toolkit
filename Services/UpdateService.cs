using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.ViewModels;

namespace LbpArchiveToolkit.Services
{
    public static class UpdateService
    {
        public static async Task CheckForUpdatesAsync(IViewService viewService, HttpClient httpClient)
        {
            if ((DateTime.Now - ConfigManager.LastUpdateCheck).TotalHours < 12) return;

            try
            {
                ConfigManager.LastUpdateCheck = DateTime.Now;
                string url = "https://api.github.com/repos/GigsTheCat/LBP_Archive_Toolkit/releases/latest";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("LbpArchiveToolkit/1.0");

                using var responseMessage = await httpClient.SendAsync(request);
                responseMessage.EnsureSuccessStatusCode();

                string response = await responseMessage.Content.ReadAsStringAsync();
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
                            // Format the update message to include the patch notes
                            string message = $"A new version ({tag}) of LBP Archive Toolkit is available.\n\n";
                            
                            if (!string.IsNullOrWhiteSpace(body))
                            {
                                // Strip \r\n to standard \n for consistency, and append the notes
                                string patchNotes = body.Replace("\r\n", "\n").Trim();
                                message += $"Patch Notes:\n{patchNotes}\n\n";
                            }
                            
                            message += "Would you like to download it now?";

                            // Use the injected viewService to show the prompt
                            bool update = viewService.Confirm(message, "Update Available");
                            if (update)
                            {
                                viewService.OpenUrl("https://github.com/GigsTheCat/LBP_Archive_Toolkit/releases");
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