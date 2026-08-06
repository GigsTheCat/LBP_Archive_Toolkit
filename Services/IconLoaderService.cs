using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Utils;
using System.IO;
using System.Net.Http;
using System.Windows.Media;

namespace LbpArchiveToolkit.Services
{
    public static class IconLoaderService
    {
        public static async Task<ImageBrush?> LoadIconBrushAsync(string? hash, HttpClient client, CancellationToken token)
        {
            if (string.IsNullOrEmpty(hash) || hash.Length <= 8)
            {
                return null;
            }

            try
            {
                bool useLocalArchive = ConfigManager.DownloadServer.ToLower() == "local" && !string.IsNullOrWhiteSpace(ConfigManager.LocalArchivePath);

                if (useLocalArchive)
                {
                    try
                    {
                        byte[]? rawResource = await AssetDownloader.ExtractLocalArchiveToMemoryAsync(hash, ConfigManager.LocalArchivePath, token).ConfigureAwait(false);

                        if (rawResource != null)
                        {
                            var bmp = await Task.Run(() => TextureDecoder.DecodeToBitmapSourceCentered(rawResource, -1, false), token).ConfigureAwait(false);
                            if (token.IsCancellationRequested || bmp == null) return null;

                            var brush = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
                            brush.Freeze();
                            return brush;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log("IconLoaderService.LoadIconBrushAsync (Local Archive)", ex);
                    }
                }

                string server = ConfigManager.DownloadServer;
                string url = AssetDownloader.GetDownloadUrl(hash, server);
                if (string.IsNullOrEmpty(url)) return null;

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > 5242880) return null;

                using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                int maxCapacity = 5242880; // 5 MB hard cap
                byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(maxCapacity);
                int totalBytesRead = 0;
                
                try
                {
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(totalBytesRead), token).ConfigureAwait(false)) > 0)
                    {
                        totalBytesRead += bytesRead;
                        if (totalBytesRead > maxCapacity) return null; 
                    }

                    if (token.IsCancellationRequested) return null;

                    // scaleAndCenter: false avoids creating multiple heavy format-converted pixel arrays just to display it
                    var webBmp = await Task.Run(() => TextureDecoder.DecodeToBitmapSourceCentered(buffer, totalBytesRead, false), token).ConfigureAwait(false);
                    if (webBmp == null) return null;

                    var webBrush = new ImageBrush(webBmp) { Stretch = Stretch.UniformToFill };
                    webBrush.Freeze();
                    return webBrush;
                }
                finally
                {
                    // Cleanly return the 5MB array to the .NET memory allocator instantly
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                LogManager.Log("IconLoaderService.LoadIconBrushAsync", ex);
                return null;
            }
        }
    }
}