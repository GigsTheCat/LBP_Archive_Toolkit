using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace LbpArchiveToolkit.Utils
{
    public static class Far4Archive
    {
        public static (uint head, ushort branchId, ushort branchRev, byte[] sltHash, SortedDictionary<string, byte[]> hashes) ReadSaveArchive(string bkpDir)
        {
            var msArchive = new MemoryStream();
            int chunkIndex = 0;
            while (File.Exists(Path.Combine(bkpDir, chunkIndex.ToString())))
            {
                byte[] chunk = File.ReadAllBytes(Path.Combine(bkpDir, chunkIndex.ToString()));

                bool isLast = !File.Exists(Path.Combine(bkpDir, (chunkIndex + 1).ToString()));
                int xxteaEnd = chunk.Length;
                if (isLast) xxteaEnd -= 4;

                Far4Crypto.XxteaDecrypt(chunk, xxteaEnd);
                msArchive.Write(chunk, 0, chunk.Length);
                chunkIndex++;
            }

            byte[] buffer = msArchive.ToArray();
            using var ms = new MemoryStream(buffer);
            using var br = new BinaryReader(ms);

            ms.Position = buffer.Length - 8;
            int entryCount = (int)BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));

            int footerSize = 28;
            int saveKeySize = 140;
            int fatOffset = buffer.Length - footerSize - (entryCount * 0x1c);
            int saveKeyOffset = fatOffset - saveKeySize;

            if (buffer.Length >= 32)
            {
                ms.Position = buffer.Length - 32;
                int possibleSaveKeyOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
                if (possibleSaveKeyOffset > 0 && possibleSaveKeyOffset < buffer.Length - 32)
                {
                    int expectedFatOffset = buffer.Length - 32 - (entryCount * 0x1c);
                    if (possibleSaveKeyOffset + 140 == expectedFatOffset) { footerSize = 32; saveKeySize = 140; fatOffset = expectedFatOffset; saveKeyOffset = possibleSaveKeyOffset; }
                    else if (possibleSaveKeyOffset + 144 == expectedFatOffset) { footerSize = 32; saveKeySize = 144; fatOffset = expectedFatOffset; saveKeyOffset = possibleSaveKeyOffset; }
                    else if (possibleSaveKeyOffset + 148 == expectedFatOffset) { footerSize = 32; saveKeySize = 148; fatOffset = expectedFatOffset; saveKeyOffset = possibleSaveKeyOffset; }
                    else if (possibleSaveKeyOffset + 152 == expectedFatOffset) { footerSize = 32; saveKeySize = 152; fatOffset = expectedFatOffset; saveKeyOffset = possibleSaveKeyOffset; }
                }
            }

            ms.Position = saveKeyOffset;
            uint head = BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
            ushort branchId = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
            ushort branchRev = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));

            int hashOffset = 0x50 + (saveKeySize - 140);
            ms.Position = saveKeyOffset + hashOffset;
            byte[] sltHash = br.ReadBytes(20);

            ms.Position = fatOffset;
            var hashes = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            Span<byte> hashBuf = stackalloc byte[20];
            Span<byte> uintBuf = stackalloc byte[4];

            for (int i = 0; i < entryCount; i++)
            {
                ms.ReadExactly(hashBuf);
                string hash = Convert.ToHexStringLower(hashBuf);
                
                ms.ReadExactly(uintBuf);
                uint offset = BinaryPrimitives.ReadUInt32BigEndian(uintBuf);
                
                ms.ReadExactly(uintBuf);
                uint size = BinaryPrimitives.ReadUInt32BigEndian(uintBuf);

                if (offset + size > buffer.Length) continue;

                byte[] data = new byte[size];
                Array.Copy(buffer, offset, data, 0, size);
                hashes[hash] = data;
            }

            return (head, branchId, branchRev, sltHash, hashes);
        }

        public static void MakeSaveArchive(uint head, ushort branchId, ushort branchRev, byte[] sltHash, SortedDictionary<string, byte[]> hashes, string bkpDir, CancellationToken token = default)
        {
            int requiredCapacity = hashes.Sum(x => x.Value.Length) + 256 + (hashes.Count * 28);
            using var arc = new MemoryStream(requiredCapacity);
            var entries = new List<(byte[] hash, uint offset, uint size)>();

            foreach (var kvp in hashes)
            {
                uint offset = (uint)arc.Position;
                arc.Write(kvp.Value, 0, kvp.Value.Length);
                entries.Add((Convert.FromHexString(kvp.Key), offset, (uint)kvp.Value.Length));
            }

            uint pad = (uint)(arc.Position % 4);
            if (pad != 0) arc.Write(new byte[4 - pad], 0, (int)(4 - pad));

            var w = new BinaryWriter(arc);
            w.WriteUInt32BE(head);
            w.WriteUInt16BE(branchId);
            w.WriteUInt16BE(branchRev);
            w.WriteUInt32BE(1);
            w.WriteUInt32BE(0); 
            w.WriteUInt32BE(0); 
            w.Write(new byte[4 * 10]);
            w.WriteUInt32BE(0);
            w.WriteUInt32BE(29);
            w.Write(new byte[4 * 3]);
            w.Write(sltHash);
            w.Write(new byte[4 * 10]);

            foreach (var entry in entries)
            {
                w.Write(entry.hash);
                w.WriteUInt32BE(entry.offset);
                w.WriteUInt32BE(entry.size);
            }

            uint hashinateOffset = (uint)arc.Position;
            w.Write(new byte[0x14]);
            w.WriteUInt32BE((uint)entries.Count);
            w.Write(Encoding.ASCII.GetBytes("FAR4"));
            w.Flush();

            if (!arc.TryGetBuffer(out ArraySegment<byte> buffer)) throw new InvalidOperationException("Could not get stream buffer.");

            byte[] hashinateKey = new byte[] { 0x2A, 0xFD, 0xA3, 0xCA, 0x86, 0x02, 0x19, 0xB3, 0xE6, 0x8A, 0xFF, 0xCC, 0x82, 0xC7, 0x6B, 0x8A, 0xFE, 0x0A, 0xD8, 0x13, 0x5F, 0x60, 0x47, 0x5B, 0xDF, 0x5D, 0x37, 0xBC, 0x57, 0x1C, 0xB5, 0xE7, 0x96, 0x75, 0xD5, 0x28, 0xA2, 0xFA, 0x90, 0xED, 0xDF, 0xA3, 0x45, 0xB4, 0x1F, 0xF9, 0x1F, 0x25, 0xE7, 0x42, 0x45, 0x3B, 0x2B, 0xB5, 0x3E, 0x16, 0xC9, 0x58, 0x19, 0x7B, 0xE7, 0x18, 0xC0, 0x80 };
            int finalLength = (int)arc.Length;
            
            byte[] mac = HMACSHA1.HashData(hashinateKey, new ReadOnlySpan<byte>(buffer.Array!, buffer.Offset, finalLength));

            arc.Position = hashinateOffset;
            arc.Write(mac, 0, mac.Length);

            int chunkSize = 0x240000;
            int numChunks = (finalLength + chunkSize - 1) / chunkSize;

            Parallel.For(0, numChunks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token }, i =>
            {
                int start = i * chunkSize;
                int len = Math.Min(chunkSize, finalLength - start);

                byte[] chunk = System.Buffers.ArrayPool<byte>.Shared.Rent(len);
                try
                {
                    Array.Copy(buffer.Array!, buffer.Offset + start, chunk, 0, len);
                    int xxteaEnd = len;
                    if (i == numChunks - 1) xxteaEnd -= 4;

                    Far4Crypto.XxteaEncrypt(chunk, xxteaEnd);

                    using SafeFileHandle handle = File.OpenHandle(Path.Combine(bkpDir, i.ToString()), FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.None);
                    RandomAccess.Write(handle, chunk.AsSpan(0, len), 0);
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(chunk);
                }
            });
        }
    }
}