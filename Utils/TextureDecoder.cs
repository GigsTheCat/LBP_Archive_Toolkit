using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Pfim;

namespace LbpArchiveToolkit.Utils
{
    /// <summary>
    /// Handles the decryption and formatting of proprietary LBP texture files into standard image formats.
    /// </summary>
    public static class TextureDecoder
    {
        #region Public API

        /// <summary>
        /// Reads and decompresses the chunked Zlib/Deflate payloads found in LBP TEX/GTF files.
        /// </summary>
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
            
            var chunkInfos = new List<(ushort comp, ushort decomp)>();
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
                
                // If chunk is not compressed, read directly into final buffer (Zero Allocation)
                if (info.comp == info.decomp)
                {
                    br.Read(finalData, currentPos, info.comp);
                }
                else
                {
                    // If compressed, route payload through Zlib Stream
                    byte[] deflatedData = br.ReadBytes(info.comp);
                    using var msIn = new MemoryStream(deflatedData);
                    using var zlib = new ZLibStream(msIn, CompressionMode.Decompress);
                    
                    int bytesRead = 0;
                    while (bytesRead < info.decomp)
                    {
                        int r = zlib.Read(finalData, currentPos + bytesRead, info.decomp - bytesRead);
                        if (r == 0) break;
                        bytesRead += r;
                    }
                }
                currentPos += info.decomp;
            }
            
            return finalData;
        }

        /// <summary>
        /// Translates decoded raw DDS bytes into a strictly formatted PNG tailored for the PS3 XMB UI.
        /// </summary>
        public static byte[] ConvertDdsToPng(byte[] ddsData)
        {
            using var ddsStream = new MemoryStream(ddsData);
            using var pfimImage = Pfimage.FromStream(ddsStream);

            // Determine correct native Windows pixel format based on Pfim's decode
            PixelFormat format = pfimImage.Format == Pfim.ImageFormat.Rgba32 
                ? PixelFormat.Format32bppArgb 
                : PixelFormat.Format24bppRgb;

            // 1. Load raw DDS bytes instantly into a Bitmap via direct memory copying
            using var sourceBmp = new Bitmap(pfimImage.Width, pfimImage.Height, format);
            var bmpData = sourceBmp.LockBits(new Rectangle(0, 0, sourceBmp.Width, sourceBmp.Height), ImageLockMode.WriteOnly, format);
            Marshal.Copy(pfimImage.Data, 0, bmpData.Scan0, pfimImage.DataLen);
            sourceBmp.UnlockBits(bmpData);

            // 2. Create the strict 320x176 PS3 XMB standard canvas
            using var finalBmp = new Bitmap(320, 176, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(finalBmp);
            
            // 3. Fill with transparent background and draw original centered
            g.Clear(Color.Transparent);
            int x = (320 - sourceBmp.Width) / 2;
            int y = (176 - sourceBmp.Height) / 2;
            g.DrawImage(sourceBmp, x, y, sourceBmp.Width, sourceBmp.Height);

            // 4. Save directly out to PNG format
            using var ms = new MemoryStream();
            finalBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            
            return ms.ToArray();
        }

        #endregion

        #region Internal Utilities

        private static ushort BigEndianUInt16(BinaryReader br)
        {
            byte[] b = br.ReadBytes(2);
            return (ushort)((b[0] << 8) | b[1]);
        }

        #endregion
    }
}