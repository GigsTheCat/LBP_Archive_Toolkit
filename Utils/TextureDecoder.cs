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
            
            string typeStr = System.Text.Encoding.ASCII.GetString(resrcType);
            if (typeStr != "TEX" && typeStr != "GTF") throw new InvalidDataException("Unsupported texture type: " + typeStr);
                
            byte format = 0;
            ushort width = 0, height = 0, mipCount = 1;
            
            if (typeStr == "GTF")
            {
                br.BaseStream.Position = 0x14; // GTF header starts exactly 20 bytes into the stream
                format = br.ReadByte();
                mipCount = br.ReadByte();
                br.ReadBytes(6); // Skip dimension, cubemap, remap
                width = BigEndianUInt16(br);
                height = BigEndianUInt16(br);
                
                br.BaseStream.Position = 44; // Jump straight into chunk tables
            }
            
            br.ReadUInt16(); 
            ushort numChunks = BigEndianUInt16(br);
            
            var chunkInfos = new System.Collections.Generic.List<(ushort comp, ushort decomp)>();
            long totalDecompSize = 0;
            
            for (int i = 0; i < numChunks; i++)
            {
                ushort compSize = BigEndianUInt16(br);
                ushort decompSize = BigEndianUInt16(br);
                chunkInfos.Add((compSize, decompSize));
                totalDecompSize += decompSize;
            }

            const long MaxAllowedTextureSize = 32 * 1024 * 1024; // 32 MB limit
            if (totalDecompSize > MaxAllowedTextureSize || totalDecompSize < 0)
            {
                throw new InvalidDataException("Decompressed texture size exceeds safety limits.");
            }
            
            byte[] finalData = new byte[(int)totalDecompSize];
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
            
            if (method == 's' || method == 'w')
            {
                finalData = Unswizzle(finalData, format, width, height, mipCount);
            }
            
            if (typeStr == "GTF")
            {
                byte[] ddsHeader = GenerateDdsHeader(format, width, height, mipCount);
                byte[] combined = new byte[ddsHeader.Length + finalData.Length];
                Buffer.BlockCopy(ddsHeader, 0, combined, 0, ddsHeader.Length);
                Buffer.BlockCopy(finalData, 0, combined, ddsHeader.Length, finalData.Length);
                finalData = combined;
            }
            
            return finalData;
        }

        private static byte[] Unswizzle(byte[] data, byte format, int width, int height, int mipCount)
        {
            int bytesPerBlock = 16;
            int blockWidth = 4;
            int blockHeight = 4;

            // Determine block constraints from CellGcmEnumForGtf formats
            if (format == 0x81) { bytesPerBlock = 1; blockWidth = 1; blockHeight = 1; }
            else if (format == 0x85) { bytesPerBlock = 4; blockWidth = 1; blockHeight = 1; }
            else if (format == 0x86) { bytesPerBlock = 8; blockWidth = 4; blockHeight = 4; }
            else if (format == 0x87 || format == 0x88) { bytesPerBlock = 16; blockWidth = 4; blockHeight = 4; }
            else return data; 

            byte[] unswizzled = new byte[data.Length];
            int srcOffset = 0;

            for (int mip = 0; mip < mipCount; mip++)
            {
                int mipWidth = Math.Max(1, width >> mip);
                int mipHeight = Math.Max(1, height >> mip);

                int blocksX = (mipWidth + blockWidth - 1) / blockWidth;
                int blocksY = (mipHeight + blockHeight - 1) / blockHeight;
                
                int totalBlocks = blocksX * blocksY;
                int mipDataSize = totalBlocks * bytesPerBlock;

                if (srcOffset + mipDataSize > data.Length) break;

                for (int i = 0; i < totalBlocks; i++)
                {
                    uint x = Compact1By1((uint)i);
                    uint y = Compact1By1((uint)(i >> 1));

                    int destIndex = srcOffset + ((int)y * blocksX + (int)x) * bytesPerBlock;
                    int srcIndex = srcOffset + i * bytesPerBlock;

                    if (destIndex + bytesPerBlock <= unswizzled.Length)
                    {
                        Buffer.BlockCopy(data, srcIndex, unswizzled, destIndex, bytesPerBlock);
                    }
                }

                srcOffset += mipDataSize;
            }
            
            if (srcOffset < data.Length)
            {
                Buffer.BlockCopy(data, srcOffset, unswizzled, srcOffset, data.Length - srcOffset);
            }

            return unswizzled;
        }

        private static uint Compact1By1(uint x)
        {
            x &= 0x55555555;
            x = (x ^ (x >> 1)) & 0x33333333;
            x = (x ^ (x >> 2)) & 0x0f0f0f0f;
            x = (x ^ (x >> 4)) & 0x00ff00ff;
            x = (x ^ (x >> 8)) & 0x0000ffff;
            return x;
        }

        private static byte[] GenerateDdsHeader(byte format, int width, int height, int mipCount)
        {
            byte[] header = new byte[128];
            using var ms = new MemoryStream(header);
            using var bw = new BinaryWriter(ms);

            bw.Write(0x20534444); // "DDS " Little-Endian string
            bw.Write(124); 
            
            uint flags = 0x1007; 
            if (mipCount > 1) flags |= 0x20000; 
            
            bw.Write(flags);
            bw.Write(height);
            bw.Write(width);
            bw.Write(0); 
            bw.Write(0); 
            bw.Write(mipCount);
            
            ms.Position = 0x4C; 
            bw.Write(32); 
            
            uint pfFlags = 0, fourCC = 0, rgbBitCount = 0;
            uint rBitMask = 0, gBitMask = 0, bBitMask = 0, aBitMask = 0;

            if (format == 0x81) 
            {
                pfFlags = 0x2; 
                rgbBitCount = 8;
                aBitMask = 0xFF;
            }
            else if (format == 0x85) 
            {
                pfFlags = 0x41; 
                rgbBitCount = 32;
                aBitMask = 0xFF000000;
                rBitMask = 0x00FF0000;
                gBitMask = 0x0000FF00;
                bBitMask = 0x000000FF;
            }
            else if (format == 0x86) 
            {
                pfFlags = 0x4; 
                fourCC = 0x31545844; 
            }
            else if (format == 0x87) 
            {
                pfFlags = 0x4; 
                fourCC = 0x33545844; 
            }
            else if (format == 0x88) 
            {
                pfFlags = 0x4; 
                fourCC = 0x35545844; 
            }
            else
            {
                pfFlags = 0x4; 
                fourCC = 0x35545844;
            }

            bw.Write(pfFlags);
            bw.Write(fourCC);
            bw.Write(rgbBitCount);
            bw.Write(rBitMask);
            bw.Write(gBitMask);
            bw.Write(bBitMask);
            bw.Write(aBitMask);

            ms.Position = 0x6C; 
            uint caps1 = 0x1000; 
            if (mipCount > 1) caps1 |= 0x400008; 
            
            bw.Write(caps1);

            return header;
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