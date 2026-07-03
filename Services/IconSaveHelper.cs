using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Utils;

namespace LbpArchiveToolkit.Services
{
    public static class IconSaveHelper
    {
        public static async Task SaveLevelIconAsync(string? iconHash, SortedDictionary<string, byte[]> resources, string bkpPath, HttpClient client, CancellationToken token)
        {
            bool iconSaved = false;
            bool isIconGuid = !string.IsNullOrEmpty(iconHash) && iconHash.Length <= 8;

            if (!string.IsNullOrEmpty(iconHash) && !isIconGuid)
            {
                string iconHashStr = iconHash.ToLowerInvariant();
                try
                {
                    if (resources.TryGetValue(iconHashStr, out byte[]? iconResrc) && iconResrc != null)
                    {
                        await Task.Run(() =>
                        {
                            byte[] pngBytes = TextureDecoder.DecodeToPngCentered(iconResrc);
                            File.WriteAllBytes(Path.Combine(bkpPath, "ICON0.PNG"), pngBytes);
                        }).ConfigureAwait(false);
                        iconSaved = true;
                    }
                }
                catch
                {
                    try
                    {
                        string server = ConfigManager.DownloadServer;
                        string url = AssetDownloader.GetDownloadUrl(iconHash, server);

                        if (!string.IsNullOrEmpty(url))
                        {
                            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                            if (response.IsSuccessStatusCode)
                            {
                                if (response.Content.Headers.ContentLength > 5242880) throw new InvalidOperationException("Icon too large");
                                byte[] rawBytes = await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);

                                byte[] pngData = await Task.Run(() => TextureDecoder.DecodeToPngCentered(rawBytes), token).ConfigureAwait(false);
                                await File.WriteAllBytesAsync(Path.Combine(bkpPath, "ICON0.PNG"), pngData, token).ConfigureAwait(false);
                                iconSaved = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LbpArchiveToolkit.LogManager.Log("IconSaveHelper.SaveLevelIconAsync", ex);
                    }
                }
            }

            if (!iconSaved) CreatePlaceholderIcon(Path.Combine(bkpPath, "ICON0.PNG"));
        }

        public static void CreatePlaceholderIcon(string path)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using Stream? stream = assembly.GetManifestResourceStream("LbpArchiveToolkit.Assets.MissingIcon.png");

                if (stream != null)
                {
                    using var fs = File.Create(path);
                    stream.CopyTo(fs);
                    return;
                }
            }
            catch (Exception ex)
            {
                LbpArchiveToolkit.LogManager.Log("IconSaveHelper.CreatePlaceholderIcon", ex);
            }

            int width = 320; int height = 176; int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 169; pixels[i + 1] = 169; pixels[i + 2] = 169; pixels[i + 3] = 255; }

            var source = System.Windows.Media.Imaging.BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
            source.Freeze();
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));

            using var fileStream = File.Create(path);
            encoder.Save(fileStream);
        }
    }
}