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
                            var bmp = await Task.Run(() => TextureDecoder.DecodeToBitmapSourceCentered(rawResource), token).ConfigureAwait(false);
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
                using var ms = new MemoryStream(contentLength.HasValue ? (int)contentLength.Value : 81920);
                byte[] chunk = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
                
                try
                {
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
                    {
                        ms.Write(chunk, 0, bytesRead);
                        if (ms.Length > 5242880) return null; // Hard cap at 5MB
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(chunk);
                }

                if (token.IsCancellationRequested) return null;

                if (!ms.TryGetBuffer(out ArraySegment<byte> bufferSegment))
                {
                    bufferSegment = new ArraySegment<byte>(ms.ToArray());
                }

                var webBmp = await Task.Run(() => TextureDecoder.DecodeToBitmapSourceCentered(bufferSegment.Array!, bufferSegment.Count), token).ConfigureAwait(false);
                if (webBmp == null) return null;

                var webBrush = new ImageBrush(webBmp) { Stretch = Stretch.UniformToFill };
                webBrush.Freeze();
                return webBrush;
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