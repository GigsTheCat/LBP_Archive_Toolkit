using System;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LbpArchiveToolkit.Utils
{
    /// <summary>
    /// Handles the decryption and formatting of proprietary LBP texture files into standard image formats
    /// </summary>
    public static class TextureDecoder
    {
        public static byte[] DecodeToPngCentered(byte[] resourceData)
        {
            using var ms = new MemoryStream(resourceData);
            using var br = new BinaryReader(ms);
            
            byte[] resrcType = br.ReadBytes(3);
            byte method = br.ReadByte();
            
            string typeStr = System.Text.Encoding.ASCII.GetString(resrcType);
            if (typeStr != "TEX" && typeStr != "GTF") throw new InvalidDataException("Unsupported texture type: " + typeStr);
                
            byte format = 0;
            int width = 0, height = 0, mipCount = 1;
            
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

            // TEX files wrap standard files (DDS, PNG, JPG). We extract their metadata here.
            if (typeStr == "TEX")
            {
                if (finalData.Length >= 128 && finalData[0] == 'D' && finalData[1] == 'D' && finalData[2] == 'S' && finalData[3] == ' ')
                {
                    width = BitConverter.ToInt32(finalData, 16);
                    height = BitConverter.ToInt32(finalData, 12);
                    
                    uint pfFlags = BitConverter.ToUInt32(finalData, 84);
                    uint fourCC = BitConverter.ToUInt32(finalData, 88);
                    uint bitCount = BitConverter.ToUInt32(finalData, 92);

                    if ((pfFlags & 0x4) != 0) // DDPF_FOURCC (Compressed Texture)
                    {
                        if (fourCC == 0x31545844) format = 0x86; // DXT1
                        else if (fourCC == 0x33545844) format = 0x87; // DXT3
                        else if (fourCC == 0x35545844) format = 0x88; // DXT5
                        else format = 0x86; 
                    }
                    else // Uncompressed
                    {
                        if (bitCount == 32) format = 0x89; // Little Endian BGRA
                        else if (bitCount == 8) format = 0x81; // L8
                        else format = 0x89; 
                    }

                    // Extract pixel data (skip the 128 byte DDS header)
                    byte[] pixels = new byte[finalData.Length - 128];
                    Buffer.BlockCopy(finalData, 128, pixels, 0, pixels.Length);
                    finalData = pixels;
                }
                else
                {
                    // It's a standard PNG/JPG file. Hand it directly to WPF native imaging.
                    return CenterWpfImage(finalData);
                }
            }
            else // GTF files are raw console textures that need unswizzling
            {
                if (method == 's' || method == 'w')
                {
                    finalData = Unswizzle(finalData, format, width, height, mipCount);
                }
            }

            if (width == 0 || height == 0) return new byte[0];

            // Decode DXT block formats into uncompressed raw pixels
            byte[] bgraData = DecodeFormatToBgra32(finalData, format, width, height);

            return CenterBgraToPng(bgraData, width, height);
        }

        private static byte[] DecodeFormatToBgra32(byte[] data, byte format, int width, int height)
        {
            byte[] bgra = new byte[width * height * 4];

            if (format == 0x85) // Uncompressed ARGB 32-bit (Big Endian)
            {
                for (int i = 0; i < data.Length && i < bgra.Length; i += 4)
                {
                    bgra[i]     = data[i + 3]; // B
                    bgra[i + 1] = data[i + 2]; // G
                    bgra[i + 2] = data[i + 1]; // R
                    bgra[i + 3] = data[i];     // A
                }
                return bgra;
            }
            else if (format == 0x89) // Uncompressed BGRA 32-bit (PC Little Endian)
            {
                for (int i = 0; i < data.Length && i < bgra.Length; i += 4)
                {
                    bgra[i]     = data[i];     // B
                    bgra[i + 1] = data[i + 1]; // G
                    bgra[i + 2] = data[i + 2]; // R
                    bgra[i + 3] = data[i + 3]; // A
                }
                return bgra;
            }
            else if (format == 0x81) // Uncompressed 8-bit (Grayscale mapping)
            {
                for (int i = 0; i < data.Length && i * 4 < bgra.Length; i++)
                {
                    byte val = data[i];
                    bgra[i * 4]     = val;
                    bgra[i * 4 + 1] = val;
                    bgra[i * 4 + 2] = val;
                    bgra[i * 4 + 3] = 255;
                }
                return bgra;
            }

            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;
            int srcOffset = 0;

            uint[] dest = new uint[width * height];

            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    if (format == 0x86) // DXT1
                    {
                        if (srcOffset + 8 > data.Length) break;
                        DecodeDXT1Block(data, srcOffset, dest, bx, by, width, height, true);
                        srcOffset += 8;
                    }
                    else if (format == 0x87) // DXT3
                    {
                        if (srcOffset + 16 > data.Length) break;
                        DecodeDXT3Block(data, srcOffset, dest, bx, by, width, height);
                        srcOffset += 16;
                    }
                    else if (format == 0x88) // DXT5
                    {
                        if (srcOffset + 16 > data.Length) break;
                        DecodeDXT5Block(data, srcOffset, dest, bx, by, width, height);
                        srcOffset += 16;
                    }
                    else
                    {
                        // Fill unknown blocks with Magenta
                        for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++)
                        {
                            if (by * 4 + y < height && bx * 4 + x < width)
                                dest[(by * 4 + y) * width + (bx * 4 + x)] = 0xFFFF00FF; 
                        }
                    }
                }
            }

            Buffer.BlockCopy(dest, 0, bgra, 0, bgra.Length);
            return bgra;
        }

        private static void DecodeDXT1Block(byte[] data, int srcOffset, uint[] dest, int bx, int by, int width, int height, bool isDxt1)
        {
            ushort c0 = (ushort)(data[srcOffset] | (data[srcOffset + 1] << 8));
            ushort c1 = (ushort)(data[srcOffset + 2] | (data[srcOffset + 3] << 8));

            uint[] colors = new uint[4];
            colors[0] = RGB565toBGRA(c0);
            colors[1] = RGB565toBGRA(c1);

            if (c0 > c1 || !isDxt1)
            {
                colors[2] = MixColors(colors[0], colors[1], 2, 1, 3);
                colors[3] = MixColors(colors[0], colors[1], 1, 2, 3);
            }
            else
            {
                colors[2] = MixColors(colors[0], colors[1], 1, 1, 2);
                colors[3] = 0;
            }

            uint indices = (uint)(data[srcOffset + 4] | (data[srcOffset + 5] << 8) | (data[srcOffset + 6] << 16) | (data[srcOffset + 7] << 24));

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    uint idx = indices & 3;
                    indices >>= 2;
                    if (by * 4 + y >= height || bx * 4 + x >= width) continue;
                    dest[(by * 4 + y) * width + (bx * 4 + x)] = colors[idx];
                }
            }
        }

        private static void DecodeDXT3Block(byte[] data, int srcOffset, uint[] dest, int bx, int by, int width, int height)
        {
            DecodeDXT1Block(data, srcOffset + 8, dest, bx, by, width, height, false);

            for (int y = 0; y < 4; y++)
            {
                uint rowAlpha = (uint)(data[srcOffset + y * 2] | (data[srcOffset + y * 2 + 1] << 8));
                for (int x = 0; x < 4; x++)
                {
                    uint a = rowAlpha & 0xF;
                    rowAlpha >>= 4;
                    a = (a << 4) | a;
                    
                    if (by * 4 + y >= height || bx * 4 + x >= width) continue;
                    int px = (by * 4 + y) * width + (bx * 4 + x);
                    uint c = dest[px];
                    dest[px] = (c & 0x00FFFFFF) | (a << 24);
                }
            }
        }

        private static void DecodeDXT5Block(byte[] data, int srcOffset, uint[] dest, int bx, int by, int width, int height)
        {
            DecodeDXT1Block(data, srcOffset + 8, dest, bx, by, width, height, false);

            int a0 = data[srcOffset];
            int a1 = data[srcOffset + 1];
            
            int[] alphas = new int[8];
            alphas[0] = a0;
            alphas[1] = a1;
            if (a0 > a1)
            {
                for (int i = 2; i < 8; i++) alphas[i] = ((8 - i) * a0 + (i - 1) * a1) / 7;
            }
            else
            {
                for (int i = 2; i < 6; i++) alphas[i] = ((6 - i) * a0 + (i - 1) * a1) / 5;
                alphas[6] = 0;
                alphas[7] = 255;
            }

            ulong alphaIndices = data[srcOffset + 2] |
                                 ((ulong)data[srcOffset + 3] << 8) |
                                 ((ulong)data[srcOffset + 4] << 16) |
                                 ((ulong)data[srcOffset + 5] << 24) |
                                 ((ulong)data[srcOffset + 6] << 32) |
                                 ((ulong)data[srcOffset + 7] << 40);

            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    int idx = (int)(alphaIndices & 7);
                    alphaIndices >>= 3;
                    
                    if (by * 4 + y >= height || bx * 4 + x >= width) continue;
                    
                    uint a = (uint)alphas[idx];
                    int px = (by * 4 + y) * width + (bx * 4 + x);
                    uint c = dest[px];
                    dest[px] = (c & 0x00FFFFFF) | (a << 24);
                }
            }
        }

        private static uint MixColors(uint c0, uint c1, int w0, int w1, int div)
        {
            uint a0 = (c0 >> 24) & 0xFF, r0 = (c0 >> 16) & 0xFF, g0 = (c0 >> 8) & 0xFF, b0 = c0 & 0xFF;
            uint a1 = (c1 >> 24) & 0xFF, r1 = (c1 >> 16) & 0xFF, g1 = (c1 >> 8) & 0xFF, b1 = c1 & 0xFF;

            uint a = (a0 * (uint)w0 + a1 * (uint)w1) / (uint)div;
            uint r = (r0 * (uint)w0 + r1 * (uint)w1) / (uint)div;
            uint g = (g0 * (uint)w0 + g1 * (uint)w1) / (uint)div;
            uint b = (b0 * (uint)w0 + b1 * (uint)w1) / (uint)div;

            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        private static uint RGB565toBGRA(ushort c)
        {
            int r = (c >> 11) & 0x1F;
            int g = (c >> 5) & 0x3F;
            int b = c & 0x1F;
            r = (r << 3) | (r >> 2);
            g = (g << 2) | (g >> 4);
            b = (b << 3) | (b >> 2);
            return (uint)((255 << 24) | (r << 16) | (g << 8) | b);
        }

        private static byte[] Unswizzle(byte[] data, byte format, int width, int height, int mipCount)
        {
            int bytesPerBlock = 16;
            int blockWidth = 4;
            int blockHeight = 4;

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
        
        private static ushort BigEndianUInt16(BinaryReader br)
        {
            byte[] b = br.ReadBytes(2);
            return (ushort)((b[0] << 8) | b[1]);
        }

        private static byte[] CenterBgraToPng(byte[] bgraData, int srcWidth, int srcHeight)
        {
            int canvasWidth = 320;
            int canvasHeight = 176;
            int canvasStride = canvasWidth * 4;
            byte[] canvasPixels = new byte[canvasHeight * canvasStride];

            int offsetX = (canvasWidth - srcWidth) / 2;
            int offsetY = (canvasHeight - srcHeight) / 2;

            int startY = Math.Max(0, offsetY);
            int endY = Math.Min(canvasHeight, offsetY + srcHeight);
            int startX = Math.Max(0, offsetX);
            int endX = Math.Min(canvasWidth, offsetX + srcWidth);

            int srcStride = srcWidth * 4;

            for (int y = startY; y < endY; y++)
            {
                int srcY = y - offsetY;
                int srcOffset = (srcY * srcStride) + ((startX - offsetX) * 4);
                int destOffset = (y * canvasStride) + (startX * 4);
                int bytesToCopy = (endX - startX) * 4;
                
                int availableInSrc = bgraData.Length - srcOffset;
                if (bytesToCopy > availableInSrc) bytesToCopy = availableInSrc;
                if (bytesToCopy <= 0) continue;

                Buffer.BlockCopy(bgraData, srcOffset, canvasPixels, destOffset, bytesToCopy);
            }

            var pixelFormat = PixelFormats.Bgra32;
            var bitmapSource = BitmapSource.Create(canvasWidth, canvasHeight, 96, 96, pixelFormat, null, canvasPixels, canvasStride);
            bitmapSource.Freeze(); 

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            
            using var outStream = new MemoryStream();
            encoder.Save(outStream);
            
            return outStream.ToArray();
        }

        private static byte[] CenterWpfImage(byte[] imageData)
        {
            using var ms = new MemoryStream(imageData);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            int srcWidth = bitmap.PixelWidth;
            int srcHeight = bitmap.PixelHeight;

            int canvasWidth = 320;
            int canvasHeight = 176;
            int canvasStride = canvasWidth * 4;
            byte[] canvasPixels = new byte[canvasHeight * canvasStride];

            // Convert raw format to predictable 32-bit BGRA mapping
            var formattedBitmap = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            byte[] srcPixels = new byte[srcHeight * srcWidth * 4];
            formattedBitmap.CopyPixels(srcPixels, srcWidth * 4, 0);

            int offsetX = (canvasWidth - srcWidth) / 2;
            int offsetY = (canvasHeight - srcHeight) / 2;

            int startY = Math.Max(0, offsetY);
            int endY = Math.Min(canvasHeight, offsetY + srcHeight);
            int startX = Math.Max(0, offsetX);
            int endX = Math.Min(canvasWidth, offsetX + srcWidth);

            int srcStride = srcWidth * 4;

            for (int y = startY; y < endY; y++)
            {
                int srcY = y - offsetY;
                int srcOffset = (srcY * srcStride) + ((startX - offsetX) * 4);
                int destOffset = (y * canvasStride) + (startX * 4);
                int bytesToCopy = (endX - startX) * 4;
                
                int availableInSrc = srcPixels.Length - srcOffset;
                if (bytesToCopy > availableInSrc) bytesToCopy = availableInSrc;
                if (bytesToCopy <= 0) continue;

                Buffer.BlockCopy(srcPixels, srcOffset, canvasPixels, destOffset, bytesToCopy);
            }

            var bitmapSource = BitmapSource.Create(canvasWidth, canvasHeight, 96, 96, PixelFormats.Bgra32, null, canvasPixels, canvasStride);
            bitmapSource.Freeze(); 

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            
            using var outStream = new MemoryStream();
            encoder.Save(outStream);
            
            return outStream.ToArray();
        }
    }
}
