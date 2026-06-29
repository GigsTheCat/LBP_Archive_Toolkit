using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.Intrinsics;

namespace LbpArchiveToolkit.Utils
{
    /// <summary>
    /// Handles the decryption and formatting of proprietary LBP texture files into standard image formats.
    /// Utilizes System.Buffers.ArrayPool to heavily minimize Garbage Collection LOH fragmentation.
    /// </summary>
    public static class TextureDecoder
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

        public static byte[] DecodeToPngCentered(byte[] resourceData)
        {
            var source = DecodeToBitmapSourceCentered(resourceData);
            if (source == null) return Array.Empty<byte>();
            return EncodeToPng(source);
        }

        public static BitmapSource? DecodeToBitmapSourceCentered(byte[] resourceData)
        {
            if (resourceData == null || resourceData.Length < 4) return null;

            uint magic = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(resourceData[..4]);
            if (magic == 0x89504E47 || (magic & 0xFFFF0000) == 0xFFD80000)
            {
                return CenterWpfImageToBitmap(resourceData);
            }
            if (resourceData.Length < 44) return null; // Protection against short header buffers for GTF/DDS/TEX
            if (magic == 0x44445320)
            {
                return DecodeDdsToBitmapCentered(resourceData, resourceData.Length);
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

            // Rent from pool to completely bypass Large Object Heap fragmentation
            byte[] finalData = System.Buffers.ArrayPool<byte>.Shared.Rent((int)totalDecompSize);
            byte[]? unswizzled = null;
            byte[]? bgraData = null;

            try
            {
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
                    if (totalDecompSize >= 128 && finalData[0] == 'D' && finalData[1] == 'D' && finalData[2] == 'S' && finalData[3] == ' ')
                    {
                        return DecodeDdsToBitmapCentered(finalData, (int)totalDecompSize);
                    }
                    else
                    {
                        return CenterWpfImageToBitmap(finalData, (int)totalDecompSize);
                    }
                }
                else // GTF files are raw console textures that need unswizzling
                {
                    if (!isLinear)
                    {
                        // Unswizzle rents a new array for mapping, so swap their references so it's disposed
                        unswizzled = Unswizzle(finalData, format, width, height, mipCount);
                        var temp = finalData;
                        finalData = unswizzled;
                        unswizzled = temp;
                    }

                    // Restore 16-bit blocks back to Little-Endian for the GPU (using Vector512 / Vector256)
                    if (format == 0x86 || format == 0x87 || format == 0x88)
                    {
                        var span16 = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(finalData.AsSpan(0, (int)totalDecompSize));
                        ref ushort spanRef = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span16);
                        int i = 0;

                        if (System.Runtime.Intrinsics.Vector512.IsHardwareAccelerated && span16.Length >= 32)
                        {
                            for (; i <= span16.Length - 32; i += 32)
                            {
                                var v = System.Runtime.Intrinsics.Vector512.LoadUnsafe(ref spanRef, (nuint)i);
                                var swapped = System.Runtime.Intrinsics.Vector512.BitwiseOr(
                                    System.Runtime.Intrinsics.Vector512.ShiftRightLogical(v, 8),
                                    System.Runtime.Intrinsics.Vector512.ShiftLeft(v, 8));
                                swapped.StoreUnsafe(ref spanRef, (nuint)i);
                            }
                        }
                        else if (System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated && span16.Length >= 16)
                        {
                            for (; i <= span16.Length - 16; i += 16)
                            {
                                var v = System.Runtime.Intrinsics.Vector256.LoadUnsafe(ref spanRef, (nuint)i);
                                var swapped = System.Runtime.Intrinsics.Vector256.BitwiseOr(
                                    System.Runtime.Intrinsics.Vector256.ShiftRightLogical(v, 8),
                                    System.Runtime.Intrinsics.Vector256.ShiftLeft(v, 8));
                                swapped.StoreUnsafe(ref spanRef, (nuint)i);
                            }
                        }

                        for (; i < span16.Length; i++)
                        {
                            System.Runtime.CompilerServices.Unsafe.Add(ref spanRef, i) = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(System.Runtime.CompilerServices.Unsafe.Add(ref spanRef, i));
                        }
                    }
                }

                if (width == 0 || height == 0) return null;

                bgraData = DecodeFormatToBgra32(finalData, 0, (int)totalDecompSize, format, width, height);
                return CenterBgraToBitmap(bgraData, width, height);
            }
            finally
            {
                // Release all enormous memory buffers back to the .NET memory allocator instantly
                System.Buffers.ArrayPool<byte>.Shared.Return(finalData);
                if (unswizzled != null) System.Buffers.ArrayPool<byte>.Shared.Return(unswizzled);
                if (bgraData != null) System.Buffers.ArrayPool<byte>.Shared.Return(bgraData);
            }
        }

        private static BitmapSource? DecodeDdsToBitmapCentered(byte[] finalData, int dataLength)
        {
            if (dataLength < 12) return null;

            var span = finalData.AsSpan();
            uint headerSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4));
            int dataOffset = (int)(4 + headerSize);

            if (dataLength < dataOffset || dataLength < 128) return null;

            int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span.Slice(16));
            int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12));

            // DDS_PIXELFORMAT is located at offset 76 in the DDS_HEADER (80 absolute)
            uint pfFlags = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(80));
            uint fourCC = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(84));
            uint bitCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(88));

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

            if (width == 0 || height == 0) return null;

            byte[]? bgraData = null;
            try
            {
                bgraData = DecodeFormatToBgra32(finalData, dataOffset, dataLength - dataOffset, format, width, height);
                return CenterBgraToBitmap(bgraData, width, height);
            }
            finally
            {
                if (bgraData != null) System.Buffers.ArrayPool<byte>.Shared.Return(bgraData);
            }
        }

        private static byte[] DecodeFormatToBgra32(byte[] data, int dataOffset, int dataLength, byte format, int width, int height)
        {
            long totalPixels = (long)width * height;
            if (totalPixels <= 0 || totalPixels > 4194304) // Max 4M pixels (2048x2048) for standard PS3 textures
            {
                return Array.Empty<byte>();
            }

            byte[] bgra;
            try
            {
                bgra = System.Buffers.ArrayPool<byte>.Shared.Rent((int)totalPixels * 4);
            }
            catch (Exception ex)
            {
                LogManager.Log("TextureDecoder.DecodeFormatToBgra32", ex);
                return Array.Empty<byte>();
            }

            if (format == 0x85)
            {
                int max = Math.Min(dataLength, (int)(totalPixels * 4));
                ref byte srcRef = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(data.AsSpan(dataOffset));
                ref byte dstRef = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(bgra.AsSpan());
                int i = 0;

                if (System.Runtime.Intrinsics.Vector512.IsHardwareAccelerated && max >= 64)
                {
                    var mask = System.Runtime.Intrinsics.Vector512.Create(
                        (byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12,
                        19, 18, 17, 16, 23, 22, 21, 20, 27, 26, 25, 24, 31, 30, 29, 28,
                        35, 34, 33, 32, 39, 38, 37, 36, 43, 42, 41, 40, 47, 46, 45, 44,
                        51, 50, 49, 48, 55, 54, 53, 52, 59, 58, 57, 56, 63, 62, 61, 60);

                    for (; i <= max - 64; i += 64)
                    {
                        var v = System.Runtime.Intrinsics.Vector512.LoadUnsafe(ref srcRef, (nuint)i);
                        System.Runtime.Intrinsics.Vector512.Shuffle(v, mask).StoreUnsafe(ref dstRef, (nuint)i);
                    }
                }
                else if (System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated && max >= 32)
                {
                    var mask = System.Runtime.Intrinsics.Vector256.Create(
                        (byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12,
                        19, 18, 17, 16, 23, 22, 21, 20, 27, 26, 25, 24, 31, 30, 29, 28);

                    for (; i <= max - 32; i += 32)
                    {
                        var v = System.Runtime.Intrinsics.Vector256.LoadUnsafe(ref srcRef, (nuint)i);
                        System.Runtime.Intrinsics.Vector256.Shuffle(v, mask).StoreUnsafe(ref dstRef, (nuint)i);
                    }
                }

                var srcSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(data.AsSpan(dataOffset + i, max - i));
                var dstSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bgra.AsSpan(i, max - i));
                for (int j = 0; j < srcSpan.Length; j++)
                {
                    dstSpan[j] = System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(srcSpan[j]);
                }
                
                return bgra;
            }
            else if (format == 0x89)
            {
                int max = Math.Min(dataLength, (int)(totalPixels * 4));
                System.Runtime.CompilerServices.Unsafe.CopyBlockUnaligned(
                    ref System.Runtime.InteropServices.MemoryMarshal.GetReference(bgra.AsSpan()),
                    ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(data.AsSpan()), dataOffset),
                    (uint)max);
                return bgra;
            }
            else if (format == 0x81)
            {
                int max = Math.Min(dataLength, (int)totalPixels);
                ref byte srcRef = ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(data.AsSpan()), dataOffset);
                ref uint dstRef = ref System.Runtime.CompilerServices.Unsafe.As<byte, uint>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(bgra.AsSpan()));
                
                for (int i = 0; i < max; i++)
                {
                    uint val = System.Runtime.CompilerServices.Unsafe.Add(ref srcRef, i);
                    System.Runtime.CompilerServices.Unsafe.Add(ref dstRef, i) = val | (val << 8) | (val << 16) | 0xFF000000;
                }
                return bgra;
            }

            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;

            if (format == 0x86) // DXT1
            {
                Parallel.For(0, blocksY, by =>
                {
                    int srcOffset = dataOffset + by * blocksX * 8;
                    Span<uint> dest = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bgra.AsSpan());
                    ReadOnlySpan<byte> dataSpan = data;

                    for (int bx = 0; bx < blocksX; bx++)
                    {
                        if (srcOffset + 8 > dataOffset + dataLength) break;
                        DecodeDXT1Block(dataSpan, srcOffset, dest, bx, by, width, height, true);
                        srcOffset += 8;
                    }
                });
            }
            else if (format == 0x87) // DXT3
            {
                Parallel.For(0, blocksY, by =>
                {
                    int srcOffset = dataOffset + by * blocksX * 16;
                    Span<uint> dest = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bgra.AsSpan());
                    ReadOnlySpan<byte> dataSpan = data;

                    for (int bx = 0; bx < blocksX; bx++)
                    {
                        if (srcOffset + 16 > dataOffset + dataLength) break;
                        DecodeDXT3Block(dataSpan, srcOffset, dest, bx, by, width, height);
                        srcOffset += 16;
                    }
                });
            }
            else if (format == 0x88) // DXT5
            {
                Parallel.For(0, blocksY, by =>
                {
                    int srcOffset = dataOffset + by * blocksX * 16;
                    Span<uint> dest = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bgra.AsSpan());
                    ReadOnlySpan<byte> dataSpan = data;

                    for (int bx = 0; bx < blocksX; bx++)
                    {
                        if (srcOffset + 16 > dataOffset + dataLength) break;
                        DecodeDXT5Block(dataSpan, srcOffset, dest, bx, by, width, height);
                        srcOffset += 16;
                    }
                });
            }
            else
            {
                Parallel.For(0, blocksY, by =>
                {
                    Span<uint> dest = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(bgra.AsSpan());
                    for (int bx = 0; bx < blocksX; bx++)
                    {
                        for (int y = 0; y < 4; y++)
                            for (int x = 0; x < 4; x++)
                            {
                                if (by * 4 + y < height && bx * 4 + x < width)
                                    dest[(by * 4 + y) * width + (bx * 4 + x)] = 0xFFFF00FF;
                            }
                    }
                });
            }

            return bgra;
        }

        private static void DecodeDXT1Block(ReadOnlySpan<byte> data, int srcOffset, Span<uint> dest, int bx, int by, int width, int height, bool isDxt1)
        {
            ref byte srcRef = ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(data), srcOffset);
            ushort c0 = System.Runtime.CompilerServices.Unsafe.ReadUnaligned<ushort>(ref srcRef);
            ushort c1 = System.Runtime.CompilerServices.Unsafe.ReadUnaligned<ushort>(ref System.Runtime.CompilerServices.Unsafe.Add(ref srcRef, 2));

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
                colors[3] = 0; // Transparent Black
            }

            uint indices = System.Runtime.CompilerServices.Unsafe.ReadUnaligned<uint>(ref System.Runtime.CompilerServices.Unsafe.Add(ref srcRef, 4));

            int startY = by * 4;
            int startX = bx * 4;
            for (int y = 0; y < 4; y++)
            {
                int py = startY + y;
                if (py >= height)
                {
                    indices >>= 8;
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

        private static void DecodeDXT3Block(ReadOnlySpan<byte> data, int srcOffset, Span<uint> dest, int bx, int by, int width, int height)
        {
            DecodeDXT1Block(data, srcOffset + 8, dest, bx, by, width, height, false);

            int startY = by * 4;
            int startX = bx * 4;
            for (int y = 0; y < 4; y++)
            {
                int py = startY + y;
                if (py >= height) continue;
                int rowOffset = py * width;
                ref byte alphaRef = ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(data), srcOffset + y * 2);
                uint rowAlpha = System.Runtime.CompilerServices.Unsafe.ReadUnaligned<ushort>(ref alphaRef);
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

        private static void DecodeDXT5Block(ReadOnlySpan<byte> data, int srcOffset, Span<uint> dest, int bx, int by, int width, int height)
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

            ref byte alphaRef = ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(data), srcOffset + 2);
            ulong alphaIndices = System.Runtime.CompilerServices.Unsafe.ReadUnaligned<uint>(ref alphaRef) |
                                 ((ulong)System.Runtime.CompilerServices.Unsafe.ReadUnaligned<ushort>(ref System.Runtime.CompilerServices.Unsafe.Add(ref alphaRef, 4)) << 32);

            int startY = by * 4;
            int startX = bx * 4;
            for (int y = 0; y < 4; y++)
            {
                int py = startY + y;
                if (py >= height)
                {
                    alphaIndices >>= 12;
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

            byte[] unswizzled = System.Buffers.ArrayPool<byte>.Shared.Rent(totalUnswizzledSize);
            try
            {
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

                    Parallel.For(0, blocksY, y =>
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

                        int localDestOffset = (y * blocksX) * bytesPerBlock;
                        ReadOnlySpan<byte> localSrcSpan = data;
                        Span<byte> localDestSpan = unswizzled;

                        for (int x = 0; x < blocksX; x++)
                        {
                            int mortonIndex = colOffsetMap[x] | rowOffset;
                            int srcIndex = srcOffset + mortonIndex * bytesPerBlock;

                            if (srcIndex + bytesPerBlock <= localSrcSpan.Length && localDestOffset + bytesPerBlock <= localDestSpan.Length)
                            {
                                System.Runtime.CompilerServices.Unsafe.CopyBlockUnaligned(
                                    ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(localDestSpan), localDestOffset),
                                    ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(localSrcSpan), srcIndex),
                                    (uint)bytesPerBlock);
                            }
                            localDestOffset += bytesPerBlock;
                        }
                    });

                    destOffset += blocksX * blocksY * bytesPerBlock;
                    srcOffset += paddedMipDataSize;
                }

                return unswizzled;
            }
            catch
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(unswizzled);
                throw;
            }
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
                var bitmap = CenterWpfImageToBitmap(fileBytes, fileBytes.Length);
                if (bitmap == null) return Array.Empty<byte>();
                return EncodeToPng(bitmap);
            }
            catch (Exception ex)
            {
                LogManager.Log("TextureDecoder.CreateIconFromImage", ex);
                return Array.Empty<byte>();
            }
        }

        private static BitmapSource? CenterBgraToBitmap(byte[] bgraData, int srcWidth, int srcHeight)
        {
            if (srcWidth == 0 || srcHeight == 0) return null;
            return ScaleAndCenterBgraToBitmap(bgraData, srcWidth, srcHeight);
        }

        private static BitmapSource? CenterWpfImageToBitmap(byte[] imageData, int length = -1)
        {
            byte[]? bgra = null;
            try
            {
                using var ms = new MemoryStream(imageData, 0, length == -1 ? imageData.Length : length);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return null;

                int stride = bitmap.PixelWidth * 4;
                bgra = System.Buffers.ArrayPool<byte>.Shared.Rent(bitmap.PixelHeight * stride);

                var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                converted.CopyPixels(bgra, stride, 0);

                return ScaleAndCenterBgraToBitmap(bgra, bitmap.PixelWidth, bitmap.PixelHeight);
            }
            catch (Exception ex)
            {
                LogManager.Log("TextureDecoder.CenterWpfImageToBitmap", ex);
                return null;
            }
            finally
            {
                if (bgra != null) System.Buffers.ArrayPool<byte>.Shared.Return(bgra);
            }
        }

        private static BitmapSource ScaleAndCenterBgraToBitmap(byte[] sourceBgra, int srcWidth, int srcHeight)
        {
            int targetWidth = 320;
            int targetHeight = 176;

            if (srcWidth == targetWidth && srcHeight == targetHeight)
            {
                return CreateBitmapSource(sourceBgra, srcWidth, srcHeight);
            }

            double scaleX = (double)targetWidth / srcWidth;
            double scaleY = (double)targetHeight / srcHeight;
            double scale = Math.Min(scaleX, scaleY);

            int scaledWidth = (int)(srcWidth * scale);
            int scaledHeight = (int)(srcHeight * scale);

            byte[] targetBgra = System.Buffers.ArrayPool<byte>.Shared.Rent(targetWidth * targetHeight * 4);
            try
            {
                Array.Clear(targetBgra, 0, targetWidth * targetHeight * 4);

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

                Parallel.For(0, scaledHeight, y =>
                {
                    int srcY = (int)(y * invScale);
                    if (srcY >= srcHeight) srcY = srcHeight - 1;

                    int srcRowOffset = srcY * srcWidth;
                    int destRowOffset = (y + offsetY) * targetWidth + offsetX;

                    ReadOnlySpan<uint> sourcePixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(sourceBgra.AsSpan());
                    Span<uint> targetPixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(targetBgra.AsSpan());

                    for (int x = 0; x < scaledWidth; x++)
                    {
                        targetPixels[destRowOffset + x] = sourcePixels[srcRowOffset + srcXMap[x]];
                    }
                });

                return CreateBitmapSource(targetBgra, targetWidth, targetHeight);
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(targetBgra);
            }
        }

        public static BitmapSource CreateBitmapSource(byte[] bgraData, int width, int height)
        {
            var bitmapSource = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgraData, width * 4);
            bitmapSource.Freeze(); // Freezing allows WPF to natively cross threads perfectly safely
            return bitmapSource;
        }

        private static byte[] EncodeToPng(BitmapSource bitmapSource)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
    }
}