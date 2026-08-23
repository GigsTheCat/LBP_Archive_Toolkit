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
            bool isPs4 = File.Exists(Path.Combine(bkpDir, "L0")) || File.Exists(Path.Combine(bkpDir, "sce_sys", "param.sfo"));
            
            while (File.Exists(Path.Combine(bkpDir, isPs4 ? $"L{chunkIndex}" : chunkIndex.ToString())))
            {
                byte[] chunk = File.ReadAllBytes(Path.Combine(bkpDir, isPs4 ? $"L{chunkIndex}" : chunkIndex.ToString()));

                bool isLast = !File.Exists(Path.Combine(bkpDir, isPs4 ? $"L{chunkIndex + 1}" : (chunkIndex + 1).ToString()));
                int xxteaEnd = chunk.Length;
                if (isLast) xxteaEnd -= 4;

                Far4Crypto.XxteaDecrypt(chunk, xxteaEnd);

                msArchive.Write(chunk, 0, chunk.Length);
                chunkIndex++;
            }

            if (!msArchive.TryGetBuffer(out ArraySegment<byte> bufferSegment))
            {
                bufferSegment = new ArraySegment<byte>(msArchive.ToArray());
            }
            byte[] buffer = bufferSegment.Array!;
            int bufferLength = bufferSegment.Count;
            
            using var ms = new MemoryStream(buffer, bufferSegment.Offset, bufferLength, writable: false);
            using var br = new BinaryReader(ms);

            ms.Position = bufferLength - 4;
            uint magic = BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
            bool isVita = (magic & 0xFF) == 53; // '5'

            ms.Position = bufferLength - 8;
            int entryCount = (int)BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));

            int fragments = 0;
            if (isVita) {
                ms.Position = bufferLength - 12;
                fragments = (int)BinaryPrimitives.ReadUInt32LittleEndian(br.ReadBytes(4));
            }
            
            int footerSize = isVita ? 12 : 8;
            int saveKeySize = isVita ? (140 + 4 * fragments) : 132;
            int fatOffset = bufferLength - footerSize - 20 - (entryCount * 0x1c);
            int saveKeyOffset = fatOffset - saveKeySize;

            ms.Position = saveKeyOffset;
            
            bool isLittleEndianSaveKey = isVita;
            if (!isVita && buffer[bufferSegment.Offset + saveKeyOffset + 0x8] != 0) {
                isLittleEndianSaveKey = true;
            }
            
            uint head = isLittleEndianSaveKey ? BinaryPrimitives.ReadUInt32LittleEndian(br.ReadBytes(4)) : BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
            
            ushort branchId = 0;
            ushort branchRev = 0;
            if (isLittleEndianSaveKey)
            {
                branchId = BinaryPrimitives.ReadUInt16LittleEndian(br.ReadBytes(2));
                branchRev = BinaryPrimitives.ReadUInt16LittleEndian(br.ReadBytes(2));
            }
            else
            {
                branchId = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
                branchRev = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
            }

            int hashOffset = (isVita ? 0x50 : 0x48) + (saveKeySize - (isVita ? 140 : 132));
            ms.Position = saveKeyOffset + hashOffset;
            byte[] sltHash = br.ReadBytes(20);

            ms.Position = fatOffset;
            var hashes = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
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

                if (offset + size > bufferLength) continue;

                byte[] data = new byte[size];
                Array.Copy(buffer, bufferSegment.Offset + (int)offset, data, 0, size);
                hashes[hash] = data;
            }

            return (head, branchId, branchRev, sltHash, hashes);
        }

        public static void MakeSaveArchive(uint head, ushort branchId, ushort branchRev, byte[] sltHash, SortedDictionary<string, byte[]> hashes, string bkpDir, bool isPs4 = false, CancellationToken token = default)
        {
            int payloadSize = hashes.Sum(x => x.Value.Length);
            uint pad1 = (uint)(payloadSize % 4);
            int padBytes1 = pad1 == 0 ? 0 : (int)(4 - pad1);
            
            int entriesCount = hashes.Count;
            int fatSize = entriesCount * 28;
            
            int numChunks = 1;
            if (!isPs4)
            {
                while (true)
                {
                    int saveKeySize = 132;
                    int footerSize = 8;
                    int predictedLength = payloadSize + padBytes1 + saveKeySize + fatSize + 20 + footerSize;
                    int newChunks = (predictedLength + 0x240000 - 1) / 0x240000;
                    if (newChunks == numChunks) break;
                    numChunks = newChunks;
                }
            }

            using var arc = new MemoryStream();
            var entries = new List<(byte[] hash, uint offset, uint size)>();

            foreach (var kvp in hashes)
            {
                uint offset = (uint)arc.Position;
                arc.Write(kvp.Value, 0, kvp.Value.Length);
                entries.Add((Convert.FromHexString(kvp.Key), offset, (uint)kvp.Value.Length));
            }

            if (padBytes1 > 0) arc.Write(new byte[padBytes1], 0, padBytes1);

            var w = new BinaryWriter(arc);
            
            if (isPs4)
            {
                w.WriteUInt32LE(head);
                w.WriteUInt16LE(branchId);
                w.WriteUInt16LE(branchRev);
                w.WriteUInt32LE(1000); // local user ID — real PS4 backups use 1000 here, not an incrementing index
                w.Write(new byte[4 * 10]);
                w.WriteUInt32LE(0);
                w.WriteUInt32LE(29);
                w.Write(new byte[4 * 3]);
                w.Write(sltHash);
                w.Write(new byte[4 * 10]);
            }
            else
            {
                w.WriteUInt32BE(head);
                w.WriteUInt16BE(branchId);
                w.WriteUInt16BE(branchRev);
                w.WriteUInt32BE(1);
                w.Write(new byte[4 * 10]);
                w.WriteUInt32BE(0);
                w.WriteUInt32BE(29);
                w.Write(new byte[4 * 3]);
                w.Write(sltHash);
                w.Write(new byte[4 * 10]);
            }
 
            foreach (var entry in entries)
            {
                w.Write(entry.hash);
                // FAT offset/size are always big-endian, even on PS4 — only the save
                // key (head/branch/localUserID/copied/rootType/rootHash) is written
                // little-endian, since that's the only part treated as a native
                // memory-mapped struct. Confirmed against a real PS4 backup.
                w.WriteUInt32BE(entry.offset);
                w.WriteUInt32BE(entry.size);
            }

            uint hashinateOffset = (uint)arc.Position;
            w.Write(new byte[0x14]);
            
            w.WriteUInt32BE((uint)entries.Count);
            w.Write("FAR4"u8);
            w.Flush();

            if (!arc.TryGetBuffer(out ArraySegment<byte> buffer)) throw new InvalidOperationException("Could not get stream buffer.");

            byte[] hashinateKey = new byte[] { 0x2A, 0xFD, 0xA3, 0xCA, 0x86, 0x02, 0x19, 0xB3, 0xE6, 0x8A, 0xFF, 0xCC, 0x82, 0xC7, 0x6B, 0x8A, 0xFE, 0x0A, 0xD8, 0x13, 0x5F, 0x60, 0x47, 0x5B, 0xDF, 0x5D, 0x37, 0xBC, 0x57, 0x1C, 0xB5, 0xE7, 0x96, 0x75, 0xD5, 0x28, 0xA2, 0xFA, 0x90, 0xED, 0xDF, 0xA3, 0x45, 0xB4, 0x1F, 0xF9, 0x1F, 0x25, 0xE7, 0x42, 0x45, 0x3B, 0x2B, 0xB5, 0x3E, 0x16, 0xC9, 0x58, 0x19, 0x7B, 0xE7, 0x18, 0xC0, 0x80 };
            
            int finalLength = (int)arc.Length;
            byte[] mac = HMACSHA1.HashData(hashinateKey, new ReadOnlySpan<byte>(buffer.Array!, buffer.Offset, finalLength));

            arc.Position = hashinateOffset;
            arc.Write(mac, 0, mac.Length);

            int chunkSize = isPs4 ? finalLength : 0x240000;

            Parallel.For(0, numChunks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token }, i =>
            {
                int start = i * chunkSize;
                int len = Math.Min(chunkSize, finalLength - start);

                byte[] chunk = System.Buffers.ArrayPool<byte>.Shared.Rent(len);
                try
                {
                    Array.Copy(buffer.Array!, buffer.Offset + start, chunk, 0, len);
                    int xxteaEnd = len;
                    if (i == numChunks - 1) xxteaEnd -= 4; // Ignore magic for encryption

                    Far4Crypto.XxteaEncrypt(chunk, xxteaEnd);

                    string fileName = isPs4 ? $"L{i}" : i.ToString();
                    using SafeFileHandle handle = File.OpenHandle(Path.Combine(bkpDir, fileName), FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.None);
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