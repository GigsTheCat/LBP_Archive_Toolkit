using System;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pfim;

namespace LbpArchiveToolkit.Utils
{
    /// <summary>
    /// Handles the decryption and formatting of proprietary LBP texture files into standard image formats.
    /// </summary>
    public static class TextureDecoder
    {
        public static byte[] DecodeLbpTexture(byte[] resourceData)
        {
            using var ms = new MemoryStream(resourceData);
            using var br = new BinaryReader(ms);
            
            byte[] resrcType = br.ReadBytes(3);
            byte method = br.ReadByte();
            
            if (method != (byte)' ') throw new InvalidDataException("Not a valid texture method.");
                
            string typeStr = System.Text.Encoding.ASCII.GetString(resrcType);
            if (typeStr != "TEX" && typeStr != "GTF") throw new InvalidDataException("Unsupported texture type: " + typeStr);
                
            if (typeStr != "TEX") br.ReadBytes(24); 
            
            br.ReadUInt16(); 
            ushort numChunks = BigEndianUInt16(br);
            
            var chunkInfos = new System.Collections.Generic.List<(ushort comp, ushort decomp)>();
            int totalDecompSize = 0;
            
            for (int i = 0; i < numChunks; i++)
            {
                ushort compSize = BigEndianUInt16(br);
                ushort decompSize = BigEndianUInt16(br);
                chunkInfos.Add((compSize, decompSize));
                totalDecompSize += decompSize;
            }
            
            byte[] finalData = new byte[totalDecompSize];
            int currentPos = 0;
            
            for (int i = 0; i < numChunks; i++)
            {
                var info = chunkInfos[i];
                
                if (info.comp == info.decomp)
                {
                    br.Read(finalData, currentPos, info.comp);
                }
                else
                {
                    byte[] deflatedData = System.Buffers.ArrayPool<byte>.Shared.Rent(info.comp);
                    try
                    {
                        br.Read(deflatedData, 0, info.comp);
                        using var msIn = new MemoryStream(deflatedData, 0, info.comp);
                        using var zlib = new ZLibStream(msIn, CompressionMode.Decompress);
                        
                        int bytesRead = 0;
                        while (bytesRead < info.decomp)
                        {
                            int r = zlib.Read(finalData, currentPos + bytesRead, info.decomp - bytesRead);
                            if (r == 0) break;
                            bytesRead += r;
                        }
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(deflatedData);
                    }
                }
                currentPos += info.decomp;
            }
            
            return finalData;
        }

        public static byte[] ConvertDdsToPngCentered(byte[] ddsData)
        {
            using var ddsStream = new MemoryStream(ddsData);
            using var pfimImage = Pfimage.FromStream(ddsStream);

            int canvasWidth = 320;
            int canvasHeight = 176;
            int targetBytesPerPixel = 4;
            int canvasStride = canvasWidth * targetBytesPerPixel;
            byte[] canvasPixels = new byte[canvasHeight * canvasStride];

            int srcBytesPerPixel = pfimImage.Format == Pfim.ImageFormat.Rgba32 ? 4 : 3;
            int srcStride = pfimImage.Stride;

            int offsetX = (canvasWidth - pfimImage.Width) / 2;
            int offsetY = (canvasHeight - pfimImage.Height) / 2;

            int startY = Math.Max(0, offsetY);
            int endY = Math.Min(canvasHeight, offsetY + pfimImage.Height);
            int startX = Math.Max(0, offsetX);
            int endX = Math.Min(canvasWidth, offsetX + pfimImage.Width);

            for (int y = startY; y < endY; y++)
            {
                int srcY = y - offsetY;
                int srcXOffset = startX - offsetX;

                int srcOffset = (srcY * srcStride) + (srcXOffset * srcBytesPerPixel);
                int destOffset = (y * canvasStride) + (startX * targetBytesPerPixel);

                if (srcBytesPerPixel == 4)
                {
                    int bytesToCopy = (endX - startX) * targetBytesPerPixel;
                    Buffer.BlockCopy(pfimImage.Data, srcOffset, canvasPixels, destOffset, bytesToCopy);
                }
                else
                {
                    for (int x = startX; x < endX; x++)
                    {
                        int sOff = srcOffset + ((x - startX) * srcBytesPerPixel);
                        int dOff = destOffset + ((x - startX) * targetBytesPerPixel);

                        canvasPixels[dOff] = pfimImage.Data[sOff];
                        canvasPixels[dOff + 1] = pfimImage.Data[sOff + 1];
                        canvasPixels[dOff + 2] = pfimImage.Data[sOff + 2];
                        canvasPixels[dOff + 3] = 255;
                    }
                }
            }

            var format = PixelFormats.Bgra32;
            var bitmapSource = BitmapSource.Create(canvasWidth, canvasHeight, 96, 96, format, null, canvasPixels, canvasStride);
            bitmapSource.Freeze(); 

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            
            using var outStream = new MemoryStream();
            encoder.Save(outStream);
            
            return outStream.ToArray();
        }

        private static ushort BigEndianUInt16(BinaryReader br)
        {
            byte[] b = br.ReadBytes(2);
            return (ushort)((b[0] << 8) | b[1]);
        }
    }
}