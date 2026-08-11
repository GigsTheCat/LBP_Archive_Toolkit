using System;
using System.Buffers;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LbpArchiveToolkit.Utils
{
    public static class WpfImageHelper
    {
        public static BitmapImage LoadBitmapImage(string filePath)
        {
            var bmp = new BitmapImage();
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream;
                bmp.EndInit();
            }
            bmp.Freeze();
            return bmp;
        }

        public static byte[] CreateIconFromImage(string filePath)
        {
            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                var bmp = CenterWpfImageToBitmap(fileBytes, fileBytes.Length);
                if (bmp == null) return Array.Empty<byte>();

                int stride = bmp.PixelWidth * 4;
                byte[] bgra = ArrayPool<byte>.Shared.Rent(bmp.PixelHeight * stride);
                try
                {
                    var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                    converted.CopyPixels(bgra, stride, 0);
                    return TextureDecoder.EncodeBgraToPng(bgra, bmp.PixelWidth, bmp.PixelHeight);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(bgra);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log("WpfImageHelper.CreateIconFromImage", ex);
                return Array.Empty<byte>();
            }
        }

        public static (BitmapSource? Image, int Width, int Height) DecodeToBitmapSourceCentered(byte[] resourceData, int dataLength = -1, bool scaleAndCenter = true)
        {
            if (dataLength == -1) dataLength = resourceData.Length;
            if (resourceData == null || dataLength < 4) return (null, 0, 0);

            var (pixels, w, h) = TextureDecoder.DecodeToBgraRaw(resourceData, dataLength, scaleAndCenter);
            
            // If dimensions are 0, it means it's an external PNG/JPG file format that needs a native WPF decoder
            if (w == 0 || h == 0)
            {
                int len = pixels != null ? pixels.Length : dataLength;
                var bmp = scaleAndCenter ? CenterWpfImageToBitmap(pixels ?? resourceData, len) : LoadWpfImageRaw(pixels ?? resourceData, len, true);
                return (bmp, bmp?.PixelWidth ?? 0, bmp?.PixelHeight ?? 0);
            }

            try
            {
                return (CreateBitmapSource(pixels!, w, h), w, h);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixels!);
            }
        }

        public static BitmapSource CreateBitmapSource(byte[] bgraData, int width, int height)
        {
            var bitmap = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Bgra32, null,
                bgraData, width * 4);
            bitmap.Freeze();
            return bitmap;
        }

        private static BitmapSource? LoadWpfImageRaw(byte[] imageData, int length, bool limitSize = false)
        {
            try
            {
                using var ms = new MemoryStream(imageData, 0, length);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                if (limitSize) bitmap.DecodePixelWidth = 320;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }

        private static BitmapSource? CenterWpfImageToBitmap(byte[] imageData, int length = -1)
        {
            try
            {
                using var ms = new MemoryStream(imageData, 0, length == -1 ? imageData.Length : length);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.DecodePixelWidth = 320;
                bitmap.EndInit();
                bitmap.Freeze();

                if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return null;

                int stride = bitmap.PixelWidth * 4;
                byte[] bgra = ArrayPool<byte>.Shared.Rent(bitmap.PixelHeight * stride);

                try
                {
                    var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                    converted.CopyPixels(bgra, stride, 0);

                    byte[] scaledBgra = TextureDecoder.ScaleAndCenterBgraRaw(bgra, bitmap.PixelWidth, bitmap.PixelHeight, out int w, out int h);
                    
                    try
                    {
                        return CreateBitmapSource(scaledBgra, w, h);
                    }
                    finally
                    {
                        if (bgra != scaledBgra) ArrayPool<byte>.Shared.Return(scaledBgra);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(bgra);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log("WpfImageHelper.CenterWpfImageToBitmap", ex);
                return null;
            }
        }
    }
}