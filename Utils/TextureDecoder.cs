using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace LbpArchiveToolkit.Utils
{
    public static class TextureDecoder
    {
        public static byte[] EncodeBgraToPng(byte[] bgraPixels, int width, int height)
        {
            if (bgraPixels == null || bgraPixels.Length < width * height * 4 || width <= 0 || height <= 0)
                return Array.Empty<byte>();

            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            // 1. PNG Header Signature
            w.Write((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            // 2. IHDR Chunk (Image Header)
            Span<byte> ihdr = stackalloc byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(ihdr[0..4], (uint)width);
            BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..8], (uint)height);
            ihdr[8] = 8;  // 8 bits per channel
            ihdr[9] = 6;  // ColorType 6 = RGBA
            ihdr[10] = 0; // Compression Method (Deflate)
            ihdr[11] = 0; // Filter Method (Standard)
            ihdr[12] = 0; // Interlace (None)
            WritePngChunk(w, "IHDR"u8, ihdr);

            // 3. Convert BGRA to RGBA + Add Filter Byte 0x00 per scanline
            int rowStride = width * 4;
            byte[] rawScanlines = new byte[height * (rowStride + 1)];

            for (int y = 0; y < height; y++)
            {
                int srcOffset = y * rowStride;
                int dstOffset = y * (rowStride + 1);

                rawScanlines[dstOffset] = 0x00; // Filter Type 0 (None)

                for (int x = 0; x < width; x++)
                {
                    int srcPix = srcOffset + (x * 4);
                    int dstPix = dstOffset + 1 + (x * 4);

                    rawScanlines[dstPix + 0] = bgraPixels[srcPix + 2]; // R
                    rawScanlines[dstPix + 1] = bgraPixels[srcPix + 1]; // G
                    rawScanlines[dstPix + 2] = bgraPixels[srcPix + 0]; // B
                    rawScanlines[dstPix + 3] = bgraPixels[srcPix + 3]; // A
                }
            }

            // 4. IDAT Chunk (Compressed Pixel Data using BCL ZLibStream)
            using (var idatMs = new MemoryStream())
            {
                using (var zlib = new ZLibStream(idatMs, CompressionLevel.Optimal, leaveOpen: true))
                {
                    zlib.Write(rawScanlines, 0, rawScanlines.Length);
                }
                WritePngChunk(w, "IDAT"u8, idatMs.ToArray());
            }

            // 5. IEND Chunk (End of PNG)
            WritePngChunk(w, "IEND"u8, ReadOnlySpan<byte>.Empty);

            return ms.ToArray();
        }

        private static void WritePngChunk(BinaryWriter w, ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> chunkData)
        {
            w.WriteUInt32BE((uint)chunkData.Length);
            w.Write(chunkType);
            if (chunkData.Length > 0) w.Write(chunkData);

            uint crc = CalculateCrc32(chunkType, chunkData);
            w.WriteUInt32BE(crc);
        }

        private static uint CalculateCrc32(ReadOnlySpan<byte> data1, ReadOnlySpan<byte> data2)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data1.Length; i++)
            {
                crc ^= data1[i];
                for (int j = 0; j < 8; j++)
                    crc = (crc >> 1) ^ (0xEDB88320 * (crc & 1));
            }
            for (int i = 0; i < data2.Length; i++)
            {
                crc ^= data2[i];
                for (int j = 0; j < 8; j++)
                    crc = (crc >> 1) ^ (0xEDB88320 * (crc & 1));
            }
            return ~crc;
        }

        public static byte[] DecodeToPngCentered(byte[] resourceData)
        {
            var (pixels, w, h) = DecodeToBgraRaw(resourceData, -1, scaleAndCenter: true);
            if (pixels == null) return Array.Empty<byte>();

            try
            {
                return EncodeBgraToPng(pixels, w, h);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixels);
            }
        }

        private const uint MAGIC_PNG = 0x89504E47;
        private const uint MAGIC_JPEG_MASK = 0xFFFF0000;
        private const uint MAGIC_JPEG = 0xFFD80000;
        private const uint MAGIC_DDS = 0x44445320; // 'DDS '
        
        private const uint FOURCC_DXT1 = 0x31545844; // 'DXT1'
        private const uint FOURCC_DXT3 = 0x33545844; // 'DXT3'
        private const uint FOURCC_DXT5 = 0x35545844; // 'DXT5'

        private const byte FMT_B8 = 0x81;
        private const byte FMT_A8R8G8B8 = 0x85;
        private const byte FMT_DXT1 = 0x86;
        private const byte FMT_DXT3 = 0x87;
        private const byte FMT_DXT5 = 0x88;
        private const byte FMT_X8R8G8B8 = 0x89;

        public static (byte[]? pixels, int width, int height) DecodeToBgraRaw(byte[] resourceData, int dataLength = -1, bool scaleAndCenter = false)
        {
            if (dataLength == -1) dataLength = resourceData.Length;
            if (resourceData == null || dataLength < 4) return (null, 0, 0);

            uint magic = BinaryPrimitives.ReadUInt32BigEndian(resourceData.AsSpan(0, 4));
            
            if (magic == MAGIC_PNG || (magic & MAGIC_JPEG_MASK) == MAGIC_JPEG) 
            {
                // External framework must decode these standard formats
                byte[] rawFile = new byte[dataLength];
                Array.Copy(resourceData, rawFile, dataLength);
                return (rawFile, 0, 0); 
            }
            
            if (dataLength < 44) return (null, 0, 0);

            byte[]? bgraData = null;
            int width = 0, height = 0;
            byte format = 0;

            if (magic == MAGIC_DDS)
            {
                var span = resourceData.AsSpan();
                uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4));
                int dataOffset = (int)(4 + headerSize);
                if (dataLength >= dataOffset && dataLength >= 128)
                {
                    width = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(16));
                    height = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12));

                    uint pfFlags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(80));
                    uint fourCC = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(84));
                    uint bitCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(88));
                    if ((pfFlags & 0x4) != 0)
                    {
                        if (fourCC == FOURCC_DXT1) format = FMT_DXT1;
                        else if (fourCC == FOURCC_DXT3) format = FMT_DXT3;
                        else if (fourCC == FOURCC_DXT5) format = FMT_DXT5;
                        else format = FMT_DXT1;
                    }
                    else
                    {
                        format = bitCount == 8 ? FMT_B8 : FMT_X8R8G8B8;
                    }

                    if (width > 0 && height > 0)
                    {
                        bgraData = DecodeFormatToBgra32(resourceData, dataOffset, dataLength - dataOffset, format, width, height);
                    }
                }
            }
            else
            {
                using var ms = new MemoryStream(resourceData, 0, dataLength);
                using var br = new BinaryReader(ms);

                byte[] resrcType = br.ReadBytes(3);
                byte method = br.ReadByte();

                string typeStr = System.Text.Encoding.ASCII.GetString(resrcType);
                if (typeStr != "TEX" && typeStr != "GTF") return (null, 0, 0);

                int mipCount = 1;
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
                        // Safely copy the uncompressed DDS data out of the rented array to recursively decode it
                        byte[] embeddedDds = new byte[totalDecompSize];
                        Array.Copy(finalData, embeddedDds, totalDecompSize);
                        return DecodeToBgraRaw(embeddedDds, (int)totalDecompSize, scaleAndCenter);
                    }
                    else
                    {
                        // It's standard image data (PNG/JPG inside a TEX wrapper). Return raw bytes for external decoding.
                        byte[] embeddedImage = new byte[totalDecompSize];
                        Array.Copy(finalData, embeddedImage, totalDecompSize);
                        return (embeddedImage, 0, 0);
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
                    if (format == FMT_DXT1 || format == FMT_DXT3 || format == FMT_DXT5)
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

                if (width > 0 && height > 0)
                    {
                        bgraData = DecodeFormatToBgra32(finalData, 0, (int)totalDecompSize, format, width, height);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(finalData);
                    if (unswizzled != null) ArrayPool<byte>.Shared.Return(unswizzled);
                }
            }

            if (bgraData == null || bgraData.Length == 0 || width == 0 || height == 0)
                return (null, 0, 0);

            if (scaleAndCenter)
            {
                byte[] scaledBgra = ScaleAndCenterBgraRaw(bgraData, width, height, out int outW, out int outH);
                if (bgraData != scaledBgra)
                    ArrayPool<byte>.Shared.Return(bgraData);

                return (scaledBgra, outW, outH);
            }

            return (bgraData, width, height);
        }

        public static byte[] ScaleAndCenterBgraRaw(byte[] sourceBgra, int srcWidth, int srcHeight, out int targetWidth, out int targetHeight)
        {
            targetWidth = 320;
            targetHeight = 176;

            if (srcWidth == targetWidth && srcHeight == targetHeight)
            {
                return sourceBgra;
            }

            double scaleX = (double)targetWidth / srcWidth;
            double scaleY = (double)targetHeight / srcHeight;
            double scale = Math.Min(scaleX, scaleY);

            int scaledWidth = (int)(srcWidth * scale);
            int scaledHeight = (int)(srcHeight * scale);

            byte[] targetBgra = ArrayPool<byte>.Shared.Rent(targetWidth * targetHeight * 4);
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

            ReadOnlySpan<uint> sourcePixels = MemoryMarshal.Cast<byte, uint>(sourceBgra.AsSpan());
            Span<uint> targetPixels = MemoryMarshal.Cast<byte, uint>(targetBgra.AsSpan());

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

            return targetBgra;
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

            if (format == FMT_A8R8G8B8)
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
            else if (format == FMT_X8R8G8B8)
            {
                int max = Math.Min(dataLength, (int)(totalPixels * 4));
                System.Runtime.CompilerServices.Unsafe.CopyBlockUnaligned(
                    ref System.Runtime.InteropServices.MemoryMarshal.GetReference(bgra.AsSpan()),
                    ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(data.AsSpan()), dataOffset),
                    (uint)max);
                return bgra;
            }
            else if (format == FMT_B8)
            {
                int max = Math.Min(dataLength, (int)totalPixels);
                ref byte srcRef = ref System.Runtime.CompilerServices.Unsafe.Add(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(data.AsSpan()), dataOffset);
                ref uint dstRef = ref System.Runtime.CompilerServices.Unsafe.As<byte, uint>(ref System.Runtime.InteropServices.MemoryMarshal.GetReference(bgra.AsSpan()));
                int i = 0;

                if (System.Runtime.Intrinsics.Vector256.IsHardwareAccelerated && max >= 32)
                {
                    var alphaMask = System.Runtime.Intrinsics.Vector256.Create(0xFF000000);
                    for (; i <= max - 32; i += 32)
                    {
                        var srcVec = System.Runtime.Intrinsics.Vector256.LoadUnsafe(ref srcRef, (nuint)i);
                        
                        var (lower, upper) = System.Runtime.Intrinsics.Vector256.Widen(srcVec);
                        var (ll, lu) = System.Runtime.Intrinsics.Vector256.Widen(lower);
                        var (ul, uu) = System.Runtime.Intrinsics.Vector256.Widen(upper);
                        
                        var v0 = ll | (ll << 8) | (ll << 16) | alphaMask;
                        var v1 = lu | (lu << 8) | (lu << 16) | alphaMask;
                        var v2 = ul | (ul << 8) | (ul << 16) | alphaMask;
                        var v3 = uu | (uu << 8) | (uu << 16) | alphaMask;
                        
                        v0.StoreUnsafe(ref dstRef, (nuint)i);
                        v1.StoreUnsafe(ref dstRef, (nuint)(i + 8));
                        v2.StoreUnsafe(ref dstRef, (nuint)(i + 16));
                        v3.StoreUnsafe(ref dstRef, (nuint)(i + 24));
                    }
                }

                for (; i < max; i++)
                {
                    uint val = System.Runtime.CompilerServices.Unsafe.Add(ref srcRef, i);
                    System.Runtime.CompilerServices.Unsafe.Add(ref dstRef, i) = val | (val << 8) | (val << 16) | 0xFF000000;
                }
                return bgra;
            }

            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;

            if (format == FMT_DXT1) // DXT1
            {
                for (int by = 0; by < blocksY; by++)
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
                }
            }
            else if (format == FMT_DXT3) // DXT3
            {
                for (int by = 0; by < blocksY; by++)
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
                }
            }
            else if (format == FMT_DXT5) // DXT5
            {
                for (int by = 0; by < blocksY; by++)
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
                }
            }
            else
            {
                for (int by = 0; by < blocksY; by++)
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
                }
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

            if (format == FMT_B8) { bytesPerBlock = 1; blockWidth = 1; blockHeight = 1; }
            else if (format == FMT_A8R8G8B8 || format == FMT_X8R8G8B8) { bytesPerBlock = 4; blockWidth = 1; blockHeight = 1; }
            else if (format == FMT_DXT1) { bytesPerBlock = 8; blockWidth = 4; blockHeight = 4; }
            else if (format == FMT_DXT3 || format == FMT_DXT5) { bytesPerBlock = 16; blockWidth = 4; blockHeight = 4; }
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
                    }

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

        private static ushort BigEndianUInt16(BinaryReader br) => 
           System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(br.ReadUInt16());

        
    }
}