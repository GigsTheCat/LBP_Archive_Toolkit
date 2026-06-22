using System;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LbpArchiveToolkit.Utils
{
    /// <summary>
    /// Handles the decryption and formatting of proprietary LBP texture files into standard image formats.
    /// </summary>
    public static class TextureDecoder
    {
        public static byte[] DecodeToPngCentered(byte[] resourceData)
{
    if (resourceData == null || resourceData.Length < 4) return new byte[0];

    uint magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(resourceData.AsSpan(0, 4));
    if (magic == 0x89504E47 || (magic & 0xFFFF0000) == 0xFFD80000)
    {
        return CenterWpfImage(resourceData);
    }
    if (resourceData.Length < 44) return new byte[0]; // Protection against short header buffers for GTF/DDS/TEX
    if (magic == 0x44445320)
    {
        return DecodeDdsToPngCentered(resourceData);
    }

            using var ms = new MemoryStream(resourceData);
            using var br = new BinaryReader(ms);
            
            byte[] resrcType = br.ReadBytes(3);
            byte method = br.ReadByte();
            
            string typeStr = System.Text.Encoding.ASCII.GetString(resrcType);
            if (typeStr != "TEX" && typeStr != "GTF") throw new InvalidDataException("Unsupported texture type: " + typeStr);
                
            byte format = 0;
            int width = 0, height = 0, mipCount = 1;
            bool isLinear = false;
            
            if (typeStr == "GTF")
            {
                br.BaseStream.Position = 0x14; // GTF header starts exactly 20 bytes into the stream
                format = br.ReadByte();
                
                // PS3 libgcm uses the 0x20 bit to explicitly mark a texture as LINEAR in memory.
                isLinear = (format & 0x20) != 0;
                
                // Mask out the linear bit (0x20) and no-restriction bit (0x40) to get the base format
                format = (byte)(format & ~(0x20 | 0x40));
                
                mipCount = br.ReadByte();
                br.ReadBytes(6); // Skip dimension, cubemap, remap
                width = BigEndianUInt16(br);
                height = BigEndianUInt16(br);
                
                br.BaseStream.Position = 44; // Jump straight into chunk tables
            }
            else
            {
                br.BaseStream.Position = 4; // Go to the chunk count in TEX
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
                    int uncompBytesRead = br.Read(finalData, currentPos, info.comp);
                    if (uncompBytesRead != info.comp) throw new EndOfStreamException("Unexpected end of stream while reading uncompressed texture chunk.");
                }
                else
                {
                    byte[] deflatedData = System.Buffers.ArrayPool<byte>.Shared.Rent(info.comp);
                    try
                    {
                        int compBytesRead = br.Read(deflatedData, 0, info.comp);
                        if (compBytesRead != info.comp) throw new EndOfStreamException("Unexpected end of stream while reading compressed texture chunk.");
                        
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

            if (typeStr == "TEX")
            {
                if (finalData.Length >= 128 && finalData[0] == 'D' && finalData[1] == 'D' && finalData[2] == 'S' && finalData[3] == ' ')
                {
                    return DecodeDdsToPngCentered(finalData);
                }
                else
                {
                    return CenterWpfImage(finalData);
                }
            }
            else // GTF files are raw console textures that need unswizzling
            {
                // Unswizzle based purely on the hardware texture flag, ignoring flawed serialization hints.
                if (!isLinear)
                {
                    finalData = Unswizzle(finalData, format, width, height, mipCount);
                }

                // FIX 1: Restore 16-bit blocks back to Little-Endian for the GPU
                if (format == 0x86 || format == 0x87 || format == 0x88)
                {
                    for (int i = 0; i < finalData.Length - 1; i += 2)
                    {
                        byte temp = finalData[i];
                        finalData[i] = finalData[i + 1];
                        finalData[i + 1] = temp;
                    }
                }
            }

            if (width == 0 || height == 0) return new byte[0];

            byte[] bgraData = DecodeFormatToBgra32(finalData, format, width, height);

            return CenterBgraToPng(bgraData, width, height);
        }

        private static byte[] DecodeDdsToPngCentered(byte[] finalData)
        {
            if (finalData.Length < 128) return new byte[0];

            int width = BitConverter.ToInt32(finalData, 16);
            int height = BitConverter.ToInt32(finalData, 12);
            
            // Re-aligned offsets for standard DDS_PIXELFORMAT structures inside DDS files
            uint pfFlags = BitConverter.ToUInt32(finalData, 80);
            uint fourCC = BitConverter.ToUInt32(finalData, 84);
            uint bitCount = BitConverter.ToUInt32(finalData, 88);

            byte format = 0;
            if ((pfFlags & 0x4) != 0) // DDPF_FOURCC
            {
                if (fourCC == 0x31545844) format = 0x86; // DXT1
                else if (fourCC == 0x33545844) format = 0x87; // DXT3
                else if (fourCC == 0x35545844) format = 0x88; // DXT5
                else format = 0x86; 
            }
            else
            {
                if (bitCount == 32) format = 0x89;
                else if (bitCount == 8) format = 0x81;
                else format = 0x89; 
            }

            byte[] pixels = new byte[finalData.Length - 128];
            Buffer.BlockCopy(finalData, 128, pixels, 0, pixels.Length);
            
            if (width == 0 || height == 0) return new byte[0];

            byte[] bgraData = DecodeFormatToBgra32(pixels, format, width, height);
            return CenterBgraToPng(bgraData, width, height);
        }

        private static byte[] DecodeFormatToBgra32(byte[] data, byte format, int width, int height)
{
    long totalPixels = (long)width * height;
    if (totalPixels <= 0 || totalPixels > 16777216) // Max 16M pixels (e.g. 4096x4096) to prevent overflow/OOM
    {
        return new byte[0];
    }

    byte[] bgra = new byte[totalPixels * 4];

    if (format == 0x85)
    {
        for (int i = 0; i < data.Length && i < bgra.Length; i += 4)
        {
            bgra[i]     = data[i + 3];
            bgra[i + 1] = data[i + 2];
            bgra[i + 2] = data[i + 1];
            bgra[i + 3] = data[i];    
        }
        return bgra;
    }
    else if (format == 0x89)
    {
        for (int i = 0; i < data.Length && i < bgra.Length; i += 4)
        {
            bgra[i]     = data[i];    
            bgra[i + 1] = data[i + 1];
            bgra[i + 2] = data[i + 2];
            bgra[i + 3] = data[i + 3];
        }
        return bgra;
    }
    else if (format == 0x81)
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

    Span<uint> dest = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bgra.AsSpan());

            if (format == 0x86) // DXT1
            {
                for (int by = 0; by < blocksY; by++)
                for (int bx = 0; bx < blocksX; bx++)
                {
                    if (srcOffset + 8 > data.Length) break;
                    DecodeDXT1Block(data, srcOffset, dest, bx, by, width, height, true);
                    srcOffset += 8;
                }
            }
            else if (format == 0x87) // DXT3
            {
                for (int by = 0; by < blocksY; by++)
                for (int bx = 0; bx < blocksX; bx++)
                {
                    if (srcOffset + 16 > data.Length) break;
                    DecodeDXT3Block(data, srcOffset, dest, bx, by, width, height);
                    srcOffset += 16;
                }
            }
            else if (format == 0x88) // DXT5
            {
                for (int by = 0; by < blocksY; by++)
                for (int bx = 0; bx < blocksX; bx++)
                {
                    if (srcOffset + 16 > data.Length) break;
                    DecodeDXT5Block(data, srcOffset, dest, bx, by, width, height);
                    srcOffset += 16;
                }
            }
            else
            {
                for (int by = 0; by < blocksY; by++)
                for (int bx = 0; bx < blocksX; bx++)
                {
                    for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        if (by * 4 + y < height && bx * 4 + x < width)
                            dest[(by * 4 + y) * width + (bx * 4 + x)] = 0xFFFF00FF; 
                    }
                }
            }

            return bgra;
        }

        private static void DecodeDXT1Block(byte[] data, int srcOffset, Span<uint> dest, int bx, int by, int width, int height, bool isDxt1)
        {
            ushort c0 = (ushort)(data[srcOffset] | (data[srcOffset + 1] << 8));
            ushort c1 = (ushort)(data[srcOffset + 2] | (data[srcOffset + 3] << 8));

            Span<uint> colors = stackalloc uint[4];
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

            uint indices = (uint)data[srcOffset + 4] | 
                           ((uint)data[srcOffset + 5] << 8) | 
                           ((uint)data[srcOffset + 6] << 16) | 
                           ((uint)data[srcOffset + 7] << 24);

            int startY = by * 4;
            int startX = bx * 4;
            for (int y = 0; y < 4; y++)
            {
                int py = startY + y;
                if (py >= height) 
                {
                    indices >>= 8; // skip 4 pixels * 2 bits
                    continue;
                }
                int rowOffset = py * width;
                for (int x = 0; x < 4; x++)
                {
                    uint idx = indices & 3;
                    indices >>= 2;
                    int px = startX + x;
                    if (px >= width) continue;
                    dest[rowOffset + px] = colors[(int)idx];
                }
            }
        }

        private static void DecodeDXT3Block(byte[] data, int srcOffset, Span<uint> dest, int bx, int by, int width, int height)
        {
            DecodeDXT1Block(data, srcOffset + 8, dest, bx, by, width, height, false);

            int startY = by * 4;
            int startX = bx * 4;
            for (int y = 0; y < 4; y++)
            {
                int py = startY + y;
                if (py >= height) continue;
                int rowOffset = py * width;
                uint rowAlpha = (uint)data[srcOffset + y * 2] | ((uint)data[srcOffset + y * 2 + 1] << 8);
                for (int x = 0; x < 4; x++)
                {
                    uint a = rowAlpha & 0xF;
                    rowAlpha >>= 4;
                    a = (a << 4) | a;
                    
                    int px = startX + x;
                    if (px >= width) continue;
                    int destIdx = rowOffset + px;
                    uint c = dest[destIdx];
                    dest[destIdx] = (c & 0x00FFFFFF) | (a << 24);
                }
            }
        }

        private static void DecodeDXT5Block(byte[] data, int srcOffset, Span<uint> dest, int bx, int by, int width, int height)
        {
            DecodeDXT1Block(data, srcOffset + 8, dest, bx, by, width, height, false);

            int a0 = data[srcOffset];
            int a1 = data[srcOffset + 1];
            
            Span<int> alphas = stackalloc int[8];
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

            ulong alphaIndices = (ulong)data[srcOffset + 2] |
                                 ((ulong)data[srcOffset + 3] << 8) |
                                 ((ulong)data[srcOffset + 4] << 16) |
                                 ((ulong)data[srcOffset + 5] << 24) |
                                 ((ulong)data[srcOffset + 6] << 32) |
                                 ((ulong)data[srcOffset + 7] << 40);

            int startY = by * 4;
            int startX = bx * 4;
            for (int y = 0; y < 4; y++)
            {
                int py = startY + y;
                if (py >= height)
                {
                    alphaIndices >>= 12; // skip 4 pixels * 3 bits
                    continue;
                }
                int rowOffset = py * width;
                for (int x = 0; x < 4; x++)
                {
                    int idx = (int)(alphaIndices & 7);
                    alphaIndices >>= 3;
                    
                    int px = startX + x;
                    if (px >= width) continue;
                    
                    uint a = (uint)alphas[idx];
                    int destIdx = rowOffset + px;
                    uint c = dest[destIdx];
                    dest[destIdx] = (c & 0x00FFFFFF) | (a << 24);
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

        private static int NextPowerOfTwo(int value)
        {
            if (value < 1) return 1;
            int n = value - 1;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            return n + 1;
        }

        private static byte[] Unswizzle(byte[] data, byte format, int width, int height, int mipCount)
        {
            int bytesPerBlock = 16;
            int blockWidth = 4;
            int blockHeight = 4;

            if (format == 0x81) { bytesPerBlock = 1; blockWidth = 1; blockHeight = 1; }
            else if (format == 0x85 || format == 0x89) { bytesPerBlock = 4; blockWidth = 1; blockHeight = 1; }
            else if (format == 0x86) { bytesPerBlock = 8; blockWidth = 4; blockHeight = 4; }
            else if (format == 0x87 || format == 0x88) { bytesPerBlock = 16; blockWidth = 4; blockHeight = 4; }
            else return data; 

            int totalUnswizzledSize = 0;
            for (int mip = 0; mip < mipCount; mip++)
            {
                int mipWidth = Math.Max(1, width >> mip);
                int mipHeight = Math.Max(1, height >> mip);
                int blocksX = (mipWidth + blockWidth - 1) / blockWidth;
                int blocksY = (mipHeight + blockHeight - 1) / blockHeight;
                totalUnswizzledSize += blocksX * blocksY * bytesPerBlock;
            }

            byte[] unswizzled = new byte[totalUnswizzledSize];
            int srcOffset = 0;
            int destOffset = 0;

            ReadOnlySpan<byte> srcSpan = data;
            Span<byte> destSpan = unswizzled;

            for (int mip = 0; mip < mipCount; mip++)
            {
                int mipWidth = Math.Max(1, width >> mip);
                int mipHeight = Math.Max(1, height >> mip);

                int blocksX = (mipWidth + blockWidth - 1) / blockWidth;
                int blocksY = (mipHeight + blockHeight - 1) / blockHeight;
                
                int paddedWidth = NextPowerOfTwo(mipWidth);
                int paddedHeight = NextPowerOfTwo(mipHeight);
                
                int paddedBlocksX = Math.Max(1, paddedWidth / blockWidth);
                int paddedBlocksY = Math.Max(1, paddedHeight / blockHeight);

                int log2Width = System.Numerics.BitOperations.TrailingZeroCount((uint)paddedBlocksX);
                int log2Height = System.Numerics.BitOperations.TrailingZeroCount((uint)paddedBlocksY);

                int paddedMipDataSize = paddedBlocksX * paddedBlocksY * bytesPerBlock;
                if (srcOffset >= data.Length) break;

                int minLog = Math.Min(log2Width, log2Height);
                int[] colOffsetMap = new int[blocksX];
                for (int x = 0; x < blocksX; x++)
                {
                    int ix = (x & ((1 << minLog) - 1));
                    ix = (ix | (ix << 8)) & 0x00FF00FF;
                    ix = (ix | (ix << 4)) & 0x0F0F0F0F;
                    ix = (ix | (ix << 2)) & 0x33333333;
                    ix = (ix | (ix << 1)) & 0x55555555;

                    int colOffset = ix;
                    if (log2Width > log2Height)
                    {
                        colOffset |= (x >> minLog) << (2 * minLog);
                    }
                    colOffsetMap[x] = colOffset;
                }

                for (int y = 0; y < blocksY; y++)
                {
                    int iy = (y & ((1 << minLog) - 1));
                    iy = (iy | (iy << 8)) & 0x00FF00FF;
                    iy = (iy | (iy << 4)) & 0x0F0F0F0F;
                    iy = (iy | (iy << 2)) & 0x33333333;
                    iy = (iy | (iy << 1)) & 0x55555555;

                    int rowOffset = iy << 1;
                    if (log2Height > log2Width)
                    {
                        rowOffset |= (y >> minLog) << (2 * minLog);
                    }

                    for (int x = 0; x < blocksX; x++)
                    {
                        int mortonIndex = colOffsetMap[x] | rowOffset;
                        int srcIndex = srcOffset + mortonIndex * bytesPerBlock;

                        if (srcIndex + bytesPerBlock <= srcSpan.Length && destOffset + bytesPerBlock <= destSpan.Length)
                        {
                            srcSpan.Slice(srcIndex, bytesPerBlock).CopyTo(destSpan.Slice(destOffset, bytesPerBlock));
                        }
                        destOffset += bytesPerBlock;
                    }
                }

                srcOffset += paddedMipDataSize;
            }

            return unswizzled;
        }
        
        private static ushort BigEndianUInt16(BinaryReader br)
        {
            byte[] b = br.ReadBytes(2);
            return (ushort)((b[0] << 8) | b[1]);
        }

        public static byte[] CreateIconFromImage(string filePath)
        {
            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                using var ms = new MemoryStream(fileBytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                int stride = bitmap.PixelWidth * 4;
                byte[] bgra = new byte[bitmap.PixelHeight * stride];
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                converted.CopyPixels(bgra, stride, 0);

                return ScaleAndCenterBgra(bgra, bitmap.PixelWidth, bitmap.PixelHeight);
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private static byte[] CenterBgraToPng(byte[] bgraData, int srcWidth, int srcHeight)
        {
            if (srcWidth == 0 || srcHeight == 0) return new byte[0];
            return ScaleAndCenterBgra(bgraData, srcWidth, srcHeight);
        }

        private static byte[] CenterWpfImage(byte[] imageData)
        {
            try
            {
                using var ms = new MemoryStream(imageData);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return new byte[0];

                // Convert any image to BGRA32 format byte array safely
                int stride = bitmap.PixelWidth * 4;
                byte[] bgra = new byte[bitmap.PixelHeight * stride];
                
                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                converted.CopyPixels(bgra, stride, 0);

                return ScaleAndCenterBgra(bgra, bitmap.PixelWidth, bitmap.PixelHeight);
            }
            catch
            {
                return new byte[0];
            }
        }

        private static byte[] ScaleAndCenterBgra(byte[] sourceBgra, int srcWidth, int srcHeight)
        {
            int targetWidth = 320;
            int targetHeight = 176;

            if (srcWidth == targetWidth && srcHeight == targetHeight)
            {
                return EncodeToPng(sourceBgra, srcWidth, srcHeight);
            }

            double scaleX = (double)targetWidth / srcWidth;
            double scaleY = (double)targetHeight / srcHeight;
            double scale = Math.Min(scaleX, scaleY);

            int scaledWidth = (int)(srcWidth * scale);
            int scaledHeight = (int)(srcHeight * scale);

            // Create a transparent 320x176 buffer
            byte[] targetBgra = new byte[targetWidth * targetHeight * 4];
            
            int offsetX = (targetWidth - scaledWidth) / 2;
            int offsetY = (targetHeight - scaledHeight) / 2;

            double invScale = 1.0 / scale;
            int[] srcXMap = new int[scaledWidth];
            for (int x = 0; x < scaledWidth; x++)
            {
                int srcX = (int)(x * invScale);
                if (srcX >= srcWidth) srcX = srcWidth - 1;
                srcXMap[x] = srcX;
            }

            ReadOnlySpan<uint> sourcePixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(sourceBgra.AsSpan());
            Span<uint> targetPixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(targetBgra.AsSpan());

            for (int y = 0; y < scaledHeight; y++)
            {
                int srcY = (int)(y * invScale);
                if (srcY >= srcHeight) srcY = srcHeight - 1;

                int srcRowOffset = srcY * srcWidth;
                int destRowOffset = (y + offsetY) * targetWidth + offsetX;

                for (int x = 0; x < scaledWidth; x++)
                {
                    targetPixels[destRowOffset + x] = sourcePixels[srcRowOffset + srcXMap[x]];
                }
            }

            return EncodeToPng(targetBgra, targetWidth, targetHeight);
        }

        private static byte[] EncodeToPng(byte[] bgraData, int width, int height)
        {
            var bitmapSource = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgraData, width * 4);
            bitmapSource.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}