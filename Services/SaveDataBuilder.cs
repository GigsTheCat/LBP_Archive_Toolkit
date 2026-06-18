using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LbpArchiveToolkit.Configuration;
using LbpArchiveToolkit.Models;
using LbpArchiveToolkit.Utils;
using Microsoft.Win32.SafeHandles;

namespace LbpArchiveToolkit.Services
{
    /// <summary>
    /// Handles parsing LBP dependencies and serializing the downloaded assets into encrypted PS3 save archives.
    /// </summary>
    public static class SaveDataBuilder
    {
        #region State & Constants

        private static readonly uint[] TEA_KEY = new uint[] { 0x1B70CBD, 0x149607D6, 0x7F94DD5, 0x10DB8CA0 };
        private static readonly byte[] SYSCON_MANAGER_KEY = new byte[] { 0xd4, 0x13, 0xb8, 0x96, 0x63, 0xe1, 0xfe, 0x9f, 0x75, 0x14, 0x3d, 0x3b, 0xb4, 0x56, 0x52, 0x74 };
        private static readonly byte[] KEYGEN_KEY = new byte[] { 0x6b, 0x1a, 0xce, 0xa2, 0x46, 0xb7, 0x45, 0xfd, 0x8f, 0x93, 0x76, 0x3b, 0x92, 0x05, 0x94, 0xcd, 0x53, 0x48, 0x3b, 0x82 };
        private static readonly byte[] SAVEGAME_PARAM_SFO_KEY = new byte[] { 0x0c, 0x08, 0x00, 0x0e, 0x09, 0x05, 0x04, 0x04, 0x0d, 0x01, 0x0f, 0x00, 0x04, 0x06, 0x02, 0x02, 0x09, 0x06, 0x0d, 0x03 };

        #endregion

        #region Public API

        /// <summary>
        /// Reads the dependency table header of an LBP file to find sub-resources.
        /// </summary>
        public static List<string> GetDependenciesFast(byte[] data)
        {
            var deps = new List<string>();
            if (data.Length < 12) return deps;

            byte method = data[3];
            byte[] workData = data;
            int headOffset = 4;
            int tableOffsetPos = 8;

            if (method == (byte)'e')
            {
                uint size = ReadUInt32BE(data, 4);
                if (8 + size > data.Length) return deps;
                
                workData = new byte[size];
                Array.Copy(data, 8, workData, 0, (int)size);
                XxteaDecrypt(workData, (int)size);
                
                headOffset = 0;
                tableOffsetPos = 4;
            }

            if (method == (byte)'b' || method == (byte)'e')
            {
                uint head = ReadUInt32BE(workData, headOffset);
                if (head >= 0x109)
                {
                    uint tableOffset = ReadUInt32BE(workData, tableOffsetPos);
                    if (tableOffset < workData.Length)
                    {
                        int ptr = (int)tableOffset;
                        if (ptr + 4 > workData.Length) return deps;
                        
                        // Minimum size of a dependency entry is 4 bytes (for depType == 0)
                        uint count = ReadUInt32BE(workData, ptr);
                        ptr += 4;
                        
                        long maxPossibleDeps = (workData.Length - ptr) / 4;
                        uint safeCount = (uint)Math.Min(count, maxPossibleDeps);
                        
                        // Prevent OOM and avoid resizing allocations
                        deps.Capacity = (int)safeCount;
                        
                        for (int i = 0; i < safeCount; i++)
                        {
                            if (ptr >= workData.Length) break;
                            byte flags = workData[ptr++];
                            
                            // Bit 2: Contains a 4-byte GUID
                            if ((flags & 2) != 0)
                            {
                                if (ptr + 4 > workData.Length) break;
                                ptr += 4;
                            }
                            
                            // Bit 1: Contains a 20-byte SHA1 hash
                            if ((flags & 1) != 0)
                            {
                                if (ptr + 20 > workData.Length) break;
                                string hashStr = Convert.ToHexStringLower(workData.AsSpan(ptr, 20));
                                deps.Add(hashStr);
                                ptr += 20; 
                            }
                            
                            // Always followed by a 4-byte ResourceType
                            if (ptr + 4 > workData.Length) break;
                            ptr += 4;
                        }
                    }
                }
            }
            return deps;
        }

        /// <summary>
        /// Generates the necessary SLT container, encrypts the assets into FAR4 chunks, and writes the SFO/PFD metadata.
        /// </summary>
        public static async Task BuildAndWriteSaveDataAsync(LevelItem lvl, SlotInfo slotInfo, SortedDictionary<string, byte[]> resources, string backupDir, HttpClient client, CancellationToken token)
        {
            string rootHashStr = lvl.Hash!.ToLowerInvariant();
            bool isRootGuid = rootHashStr.Length <= 8;

            uint head = 0; ushort branchId = 0; ushort branchRev = 0;

            if (!isRootGuid && resources.ContainsKey(rootHashStr))
            {
                ParseResrcRevision(resources[rootHashStr], out head, out branchId, out branchRev);
            }
            else
            {
                // Fallback to safe defaults for GUID-based built-in levels
                if (slotInfo.GameVersion == 3) { head = 0x010503e2; }
                else if (slotInfo.GameVersion == 2) { head = 0x3b6; }
                else { head = 0x272; }
            }

            if (ConfigManager.ForceLbp3Backups)
            {
                slotInfo.GameVersion = 3;
                head = 0x010503e2; branchId = 0; branchRev = 0;
            }
            else if (slotInfo.GameVersion == 2 && (head & 0xFFFF) < 0x3b6 && ConfigManager.Lbp2BetaToRetail)
            {
                head = 0x3b6; branchId = 0; branchRev = 0;
            }

            byte[] sltBytes = MakeSlotList(head, branchId, branchRev, slotInfo);
            byte[] sltHash = SHA1.HashData(sltBytes);
            
            // Add slot file to resources utilizing the fast OrdinalIgnoreCase dict
            string sltHashStr = Convert.ToHexStringLower(sltHash);
            resources[sltHashStr] = sltBytes;

            string hexId = lvl.Id.ToString("X8");
            string bkpDirName = slotInfo.IsAdventurePlanet 
                ? ((slotInfo.GameVersion == 3 ? "BCES01663" : "BCES00850") + "ADVLBP3AAZ" + hexId)
                : ((slotInfo.GameVersion == 3 ? "BCES01663" : (slotInfo.GameVersion == 2 ? "BCES00850" : "BCES00141")) + "LEVEL" + hexId);

            string bkpPath = Path.Combine(backupDir, bkpDirName);
            Directory.CreateDirectory(bkpPath);

            await SaveLevelIconAsync(lvl.IconHash, resources, bkpPath, client, token).ConfigureAwait(false);

            await Task.Run(() => MakeSaveArchive(head, branchId, branchRev, sltHash, resources, bkpPath)).ConfigureAwait(false);

            byte[] sfo = MakeSfo(lvl.LevelName ?? "", bkpDirName, lvl.Creator ?? "", lvl.Description ?? "", slotInfo.GameVersion);
            await File.WriteAllBytesAsync(Path.Combine(bkpPath, "PARAM.SFO"), sfo, token).ConfigureAwait(false);

            byte[] pfd = MakePfd((ulong)(slotInfo.GameVersion == 3 ? 4 : 3), sfo, bkpPath);
            await File.WriteAllBytesAsync(Path.Combine(bkpPath, "PARAM.PFD"), pfd, token).ConfigureAwait(false);
        }

        #endregion

        #region PlayStation Save Generation

        private static async Task SaveLevelIconAsync(string? iconHash, SortedDictionary<string, byte[]> resources, string bkpPath, HttpClient client, CancellationToken token)
        {
            bool iconSaved = false;
            bool isIconGuid = !string.IsNullOrEmpty(iconHash) && iconHash.Length <= 8;

            if (!string.IsNullOrEmpty(iconHash) && !isIconGuid)
            {
                string iconHashStr = iconHash.ToLowerInvariant();
                try 
                {
                    if (resources.TryGetValue(iconHashStr, out byte[]? iconResrc) && iconResrc != null)
                    {
                        await Task.Run(() => 
                        {
                            byte[] ddsData = TextureDecoder.DecodeLbpTexture(iconResrc);
                            byte[] pngBytes = TextureDecoder.ConvertDdsToPngCentered(ddsData);
                            File.WriteAllBytes(Path.Combine(bkpPath, "ICON0.PNG"), pngBytes);
                        }).ConfigureAwait(false);
                        iconSaved = true;
                    }
                } 
                catch 
                { 
                    try 
                    {
                        byte[]? pngData = await client.GetByteArrayAsync($"https://zaprit.fish/icon/{iconHash}", token).ConfigureAwait(false);
                        if (pngData != null)
                        {
                            await File.WriteAllBytesAsync(Path.Combine(bkpPath, "ICON0.PNG"), pngData, token).ConfigureAwait(false);
                            iconSaved = true;
                        }
                    } catch { } 
                } 
            }

            if (!iconSaved) 
            {
                CreatePlaceholderIcon(Path.Combine(bkpPath, "ICON0.PNG"));
            }
        }

        private static void MakeSaveArchive(uint head, ushort branchId, ushort branchRev, byte[] sltHash, SortedDictionary<string, byte[]> hashes, string bkpDir)
        {
            using var arc = new MemoryStream();
            var entries = new List<(byte[] hash, uint offset, uint size)>();

            foreach (var kvp in hashes)
            {
                uint offset = (uint)arc.Position;
                arc.Write(kvp.Value, 0, kvp.Value.Length);

                byte[] hashBytes = StringToByteArray(kvp.Key);
                entries.Add((hashBytes, offset, (uint)kvp.Value.Length));
            }

            uint pad = (uint)(arc.Position % 4);
            if (pad != 0) { pad = 4 - pad; arc.Write(new byte[pad], 0, (int)pad); }

            var w = new BinaryWriter(arc);
            w.WriteUInt32BE(head);
            w.WriteUInt16BE(branchId);
            w.WriteUInt16BE(branchRev);
            w.WriteUInt32BE(1);
            w.WriteUInt32BE(0); // backupID
            w.WriteUInt32BE(0); // localUserID
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

            if (!arc.TryGetBuffer(out ArraySegment<byte> buffer))
            {
                throw new InvalidOperationException("Could not get stream buffer.");
            }

            byte[] hashinateKey = new byte[] { 0x2A, 0xFD, 0xA3, 0xCA, 0x86, 0x02, 0x19, 0xB3, 0xE6, 0x8A, 0xFF, 0xCC, 0x82, 0xC7, 0x6B, 0x8A, 0xFE, 0x0A, 0xD8, 0x13, 0x5F, 0x60, 0x47, 0x5B, 0xDF, 0x5D, 0x37, 0xBC, 0x57, 0x1C, 0xB5, 0xE7, 0x96, 0x75, 0xD5, 0x28, 0xA2, 0xFA, 0x90, 0xED, 0xDF, 0xA3, 0x45, 0xB4, 0x1F, 0xF9, 0x1F, 0x25, 0xE7, 0x42, 0x45, 0x3B, 0x2B, 0xB5, 0x3E, 0x16, 0xC9, 0x58, 0x19, 0x7B, 0xE7, 0x18, 0xC0, 0x80 };

            int finalLength = (int)arc.Length;
            byte[] mac;

            using (var incrementalHash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, hashinateKey))
            {
                int hashChunkSize = 0x100000; // 1 MB chunks
                int offset = buffer.Offset;
                int remaining = finalLength;

                while (remaining > 0)
                {
                    int toHash = Math.Min(remaining, hashChunkSize);
                    incrementalHash.AppendData(buffer.Array!, offset, toHash);
                    offset += toHash;
                    remaining -= toHash;
                }
                mac = incrementalHash.GetHashAndReset();
            }

            arc.Position = hashinateOffset;
            arc.Write(mac, 0, mac.Length);

            int chunkSize = 0x240000;
            int numChunks = (finalLength + chunkSize - 1) / chunkSize;

            Parallel.For(0, numChunks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                int start = i * chunkSize;
                int len = Math.Min(chunkSize, finalLength - start);

                byte[] chunk = System.Buffers.ArrayPool<byte>.Shared.Rent(len);
                try
                {
                    Array.Copy(buffer.Array!, buffer.Offset + start, chunk, 0, len);

                    int xxteaEnd = len;
                    if (i == numChunks - 1) xxteaEnd -= 4;

                    XxteaEncrypt(chunk, xxteaEnd);

                    using SafeFileHandle handle = File.OpenHandle(
                    Path.Combine(bkpDir, i.ToString()), 
                    FileMode.Create, 
                    FileAccess.Write, 
                    FileShare.None, 
                    FileOptions.None);

                    // Writes the exact Span length directly to the disk via the OS kernel
                    RandomAccess.Write(handle, chunk.AsSpan(0, len), 0);
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(chunk);
                }
            });
        }

        private static void XxteaEncrypt(byte[] data, int end)
        {
            if (end <= 0) return;
            int n = (end / 4) - 1;
            if (n < 0) return;

            uint[] v = ArrayPool<uint>.Shared.Rent(n + 1);

            try
            {
                for (int i = 0; i <= n; i++)
                {
                    v[i] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i * 4, 4));
                }

                uint sum = 0;
                uint z = v[n];
                int rounds = 6 + 52 / (n + 1);

                for (int i = 0; i < rounds; i++)
                {
                    sum += 0x9e3779b9;
                    uint e = sum >> 2;
                    for (int r = 0; r <= n; r++)
                    {
                        uint y = v[(r + 1) % (n + 1)];
                        v[r] += (((z >> 5) ^ (y << 2)) + ((y >> 3) ^ (z << 4))) ^ ((sum ^ y) + (TEA_KEY[(r ^ e) & 3] ^ z));
                        z = v[r];
                    }
                }

                for (int i = 0; i <= n; i++)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i * 4, 4), v[i]);
                }
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(v);
            }
        }

        private static void XxteaDecrypt(byte[] data, int end)
        {
            if (end <= 0) return;
            int n = (end / 4) - 1;
            if (n < 0) return;

            uint[] v = ArrayPool<uint>.Shared.Rent(n + 1);

            try
            {
                for (int i = 0; i <= n; i++)
                {
                    v[i] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i * 4, 4));
                }

                uint y = v[0];
                int rounds = 6 + 52 / (n + 1);
                uint sum = unchecked((uint)(rounds * 0x9e3779b9));

                for (int i = 0; i < rounds; i++)
                {
                    uint e = sum >> 2;
                    for (int r = n; r >= 0; r--)
                    {
                        uint z = v[r > 0 ? r - 1 : n];
                        v[r] = unchecked(v[r] - ((((z >> 5) ^ (y << 2)) + ((y >> 3) ^ (z << 4))) ^ ((sum ^ y) + (TEA_KEY[(r ^ e) & 3] ^ z))));
                        y = v[r];
                    }
                    sum = unchecked(sum - 0x9e3779b9);
                }

                for (int i = 0; i <= n; i++)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i * 4, 4), v[i]);
                }
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(v);
            }
        }

        private static byte[] MakeSfo(string displayName, string bkpName, string npHandle, string description, int gameVersion)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            string title = (gameVersion == 3 ? "LBP3" : (gameVersion == 2 ? "LBP2" : "LBP1")) + " Dry Archive Level Backup";
            string subtitle = $"{displayName} by {npHandle}";

            var entries = new List<(string key, byte fmt, uint maxLen, byte[] data)> {
                ("ACCOUNT_ID", 4, 16, Encoding.ASCII.GetBytes("0000000000000000")),
                ("ATTRIBUTE", 4, 4, new byte[] { 0, 0, 0, 0 }),
                ("CATEGORY", 4, 4, Encoding.UTF8.GetBytes("SD\0")),
                ("DETAIL", 4, 1024, TruncateStr(description, 1024)),
                ("PARAMS", 4, 1024, new byte[1024]),
                ("PARAMS2", 4, 12, new byte[12]),
                ("SAVEDATA_DIRECTORY", 4, 64, TruncateStr(bkpName, 64)),
                ("SAVEDATA_LIST_PARAM", 4, 8, TruncateStr("", 8)),
                ("SUB_TITLE", 4, 128, TruncateStr(subtitle, 128)),
                ("TITLE", 4, 128, TruncateStr(title, 128))
            };

            var keyTable = new MemoryStream();
            int[] keyOffsets = new int[10];
            for (int i = 0; i < 10; i++)
            {
                keyOffsets[i] = (int)keyTable.Position;
                byte[] kb = Encoding.ASCII.GetBytes(entries[i].key);
                keyTable.Write(kb, 0, kb.Length);
                keyTable.WriteByte(0);
            }

            var dataTable = new MemoryStream();
            var dataInfos = new (int size, int offset)[10];
            for (int i = 0; i < 10; i++)
            {
                int size = entries[i].data.Length;
                int offset = (int)dataTable.Position;
                dataTable.Write(entries[i].data, 0, size);
                int pad = (int)entries[i].maxLen - size;
                if (pad > 0) dataTable.Write(new byte[pad], 0, pad);
                dataInfos[i] = (size, offset);
            }

            w.Write(Encoding.ASCII.GetBytes("\0PSF"));
            w.Write(new byte[] { 0x01, 0x01, 0x00, 0x00 });
            w.WriteUInt32LE(0);
            w.WriteUInt32LE(0);
            w.WriteUInt32LE(10);

            for (int i = 0; i < 10; i++)
            {
                w.WriteUInt16LE((ushort)keyOffsets[i]);
                if (entries[i].fmt == 4 && entries[i].maxLen == 4 && entries[i].key == "ATTRIBUTE") w.Write(new byte[] { 0x04, 0x04 });
                else if (entries[i].key == "ACCOUNT_ID" || entries[i].key == "PARAMS" || entries[i].key == "PARAMS2") w.Write(new byte[] { 0x04, 0x00 });
                else w.Write(new byte[] { 0x04, 0x02 });

                w.WriteUInt32LE((uint)dataInfos[i].size);
                w.WriteUInt32LE(entries[i].maxLen);
                w.WriteUInt32LE((uint)dataInfos[i].offset);
            }

            uint keyTableOffset = (uint)ms.Position;
            keyTable.WriteTo(ms);
            uint pad2 = (uint)(ms.Position % 4);
            if (pad2 != 0) w.Write(new byte[4 - pad2]);

            uint dataTableOffset = (uint)ms.Position;
            dataTable.WriteTo(ms);

            long curr = ms.Position;
            ms.Position = 8;
            w.WriteUInt32LE(keyTableOffset);
            w.WriteUInt32LE(dataTableOffset);

            return ms.ToArray();
        }

        private static byte[] MakePfd(ulong version, byte[] sfo, string bkpDir)
        {
            byte[] pfKeyOrig = new byte[20];
            byte[] pfHeaderIv = new byte[16];
            byte[] pfKey = new byte[20];

            if (version == 4)
            {
                pfKey = HMACSHA1.HashData(KEYGEN_KEY, pfKeyOrig);
            }

            ulong pfIndexSize = 1;
            ulong pfEntrySize = 1;

            byte[] sfoFilename = new byte[65];
            Encoding.ASCII.GetBytes("PARAM.SFO").CopyTo(sfoFilename, 0);

            using var pfEntries = new MemoryStream();
            using var wE = new BinaryWriter(pfEntries);
            wE.WriteUInt64BE(pfIndexSize);
            wE.Write(sfoFilename);
            wE.Write(new byte[7]);
            wE.Write(new byte[64]);
            wE.Write(HMACSHA1.HashData(SAVEGAME_PARAM_SFO_KEY, sfo));
            wE.Write(new byte[20]);
            wE.Write(new byte[20]);
            wE.Write(new byte[20]);
            wE.Write(new byte[40]);
            wE.WriteUInt64BE((ulong)sfo.Length);

            using var pfIndex = new MemoryStream();
            using var wI = new BinaryWriter(pfIndex);
            wI.WriteUInt64BE(pfIndexSize);
            wI.WriteUInt64BE(pfEntrySize);
            wI.WriteUInt64BE(pfEntrySize);
            wI.WriteUInt64BE(0);

            byte[] pfEntrySigTable;
            var ms = new MemoryStream();
            ms.Write(sfoFilename, 0, sfoFilename.Length);
            ms.Write(pfEntries.ToArray(), 80, pfEntries.ToArray().Length - 80);
            pfEntrySigTable = HMACSHA1.HashData(pfKey, ms.ToArray());

            byte[] pfIndexSig = HMACSHA1.HashData(pfKey, pfIndex.ToArray());
            byte[] pfEntrySigTableSig = HMACSHA1.HashData(pfKey, pfEntrySigTable);

            byte[] pfHeader = new byte[64];
            using (var msH = new MemoryStream(pfHeader))
            {
                msH.Write(pfEntrySigTableSig, 0, pfEntrySigTableSig.Length);
                msH.Write(pfIndexSig, 0, pfIndexSig.Length);
                msH.Write(pfKeyOrig, 0, pfKeyOrig.Length);
            }

            using (var aes = Aes.Create())
            {
                aes.Key = SYSCON_MANAGER_KEY;
                aes.IV = pfHeaderIv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.None; 
                using var enc = aes.CreateEncryptor();
                pfHeader = enc.TransformFinalBlock(pfHeader, 0, pfHeader.Length);
            }

            using var pfd = new MemoryStream();
            using var wP = new BinaryWriter(pfd);
            wP.Write(new byte[] { 0, 0, 0, 0 });
            wP.Write(Encoding.ASCII.GetBytes("PFDB"));
            wP.WriteUInt64BE(version);
            wP.Write(pfHeaderIv);
            wP.Write(pfHeader);
            wP.Write(pfIndex.ToArray());
            wP.Write(pfEntries.ToArray());
            wP.Write(pfEntrySigTable);

            return pfd.ToArray();
        }

        #endregion

        #region LBP Slot Serialization

        private static void ParseResrcRevision(byte[] rootLevelData, out uint head, out ushort branchId, out ushort branchRev)
        {
            head = 0; branchId = 0; branchRev = 0;
            if (rootLevelData == null || rootLevelData.Length < 8) return;
            
            byte method = rootLevelData[3];
            if (method == 'b' || method == 'e')
            {
                head = BitConverter.ToUInt32(rootLevelData.Skip(4).Take(4).Reverse().ToArray(), 0);
                string resrcType = Encoding.ASCII.GetString(rootLevelData, 0, 3);
                
                if (resrcType != "SMH" && head >= 0x271)
                {
                    int offset = head >= 0x109 ? 12 : 8;
                    if (rootLevelData.Length >= offset + 4)
                    {
                        branchId = BitConverter.ToUInt16(rootLevelData.Skip(offset).Take(2).Reverse().ToArray(), 0);
                        branchRev = BitConverter.ToUInt16(rootLevelData.Skip(offset + 2).Take(2).Reverse().ToArray(), 0);
                    }
                }
            }
        }

        private static byte[] MakeSlotList(uint head, ushort branchId, ushort branchRev, SlotInfo info)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("SLTb"));
            w.WriteUInt32BE(head);
            if (head >= 0x109)
            {
                w.WriteUInt32BE(0); 
                if (head >= 0x189)
                {
                    if (head >= 0x271) { w.WriteUInt16BE(branchId); w.WriteUInt16BE(branchRev); }
                    if (head >= 0x297 || (head == 0x272 && branchId == 0x4c44 && branchRev >= 2)) w.Write((byte)0);
                    w.Write((byte)0);
                }
            }
            w.WriteUInt32BE(1);

            var deps = new List<Tuple<string, uint>>();
            WriteSlotStruct(w, head & 0xFFFF, (head >> 16) & 0xFFFF, info, deps);

            if ((head & 0xFFFF) >= 0x3b6) w.Write((byte)1);

            if (head >= 0x109)
            {
                long depOffset = ms.Position;
                long curr = ms.Position;
                ms.Position = 8;
                w.WriteUInt32BE((uint)depOffset);
                ms.Position = curr;

                w.WriteUInt32BE((uint)deps.Count);
                foreach (var dep in deps)
                {
                    if (dep.Item1.Length > 8)
                    {
                        w.Write((byte)1); // Always 1 for SHA1 hashes in outer dependency table
                        w.Write(StringToByteArray(dep.Item1));
                    }
                    else
                    {
                        w.Write((byte)2); // Always 2 for GUIDs in outer dependency table
                        w.WriteUInt32BE(uint.Parse(dep.Item1, System.Globalization.NumberStyles.HexNumber));
                    }
                    w.WriteUInt32BE(dep.Item2);
                }
            }
            w.Flush();
            return ms.ToArray();
        }

        private static void WriteSlotStruct(BinaryWriter writer, uint version, uint subversion, SlotInfo info, List<Tuple<string, uint>> deps)
        {
            writer.WriteUInt32BE(6); 
            writer.WriteUInt32BE(0); 

            bool isRootGuid = !string.IsNullOrEmpty(info.RootLevelHash) && info.RootLevelHash.Length <= 8;
            string? rootDesc = info.IsAdventurePlanet ? null : info.RootLevelHash;
            WriteResDescriptor(writer, version, deps, rootDesc, 9, isRootGuid);

            if (subversion >= 0x145)
            {
                string? advDesc = info.IsAdventurePlanet ? info.RootLevelHash : null;
                WriteResDescriptor(writer, version, deps, advDesc, 31, isRootGuid);
            }

            bool isIconGuid = !string.IsNullOrEmpty(info.IconHash) && info.IconHash.Length <= 8;
            WriteResDescriptor(writer, version, deps, info.IconHash, 1, isIconGuid);

            for (int i = 0; i < 4; i++) writer.WriteUInt32BE(0); 

            WriteOnlineId(writer, version, info.NpHandle);
            if (version >= 0x13b) WriteWStr(writer, info.NpHandle);
            WriteStr(writer, "");
            WriteWStr(writer, info.Name);
            WriteWStr(writer, info.Description);

            writer.WriteUInt32BE(0); writer.WriteUInt32BE(0);
            if (version >= 0x134) { writer.WriteUInt32BE(0); writer.WriteUInt32BE(0); }
            writer.Write((byte)(info.InitiallyLocked ? 1 : 0));

            if (version > 0x237)
            {
                writer.Write((byte)(info.Shareable ? 1 : 0));
                writer.WriteUInt32BE(info.BackgroundGuid);
            }
            if (version > 0x333) WriteResDescriptor(writer, version, deps, null, 38, false);
            if (version < 0x188) writer.Write((byte)0);

            if (version > 0x1de)
            {
                uint devLevelType = info.LevelType == 6 ? 6u : (info.LevelType == 7 ? 7u : 0u);
                writer.WriteUInt32BE(devLevelType);
            }
            else
            {
                writer.Write((byte)0);
            }

            if (version > 0x1ad && version < 0x1b9) writer.Write((byte)0);
            if (version > 0x1b8 && version < 0x36c) writer.WriteUInt32BE(0);

            if (version <= 0x2c3) return;

            if (version >= 0x33c)
            {
                var labelsToWrite = info.Labels;

                if (info.GameVersion == 2)
                {
                    labelsToWrite = new List<uint>();
                    foreach (var l in info.Labels)
                    {
                        if (LabelParser.IsLbp2Label(l)) labelsToWrite.Add(l);
                    }
                }

                writer.WriteUInt32BE((uint)labelsToWrite.Count);
                for (int i = 0; i < labelsToWrite.Count; i++)
                {
                    writer.WriteUInt32BE(labelsToWrite[i]);
                    writer.WriteUInt32BE((uint)i);
                }
            }

            if (version >= 0x2ea)
            {
                writer.WriteUInt32BE(3);
                for (int i = 0; i < 3; i++)
                {
                    WriteResDescriptor(writer, version, deps, null, 38, false);
                    writer.WriteUInt32BE(0);
                }
            }

            if (version >= 0x2f4) writer.WriteUInt32BE(0);
            if (version >= 0x352) writer.Write((byte)(info.IsSubLevel ? 1 : 0));
            if (version < 0x3d0) return;

            writer.Write((byte)info.MinPlayers);
            writer.Write((byte)info.MaxPlayers);
            if (subversion >= 0x215) writer.Write((byte)0);
            if (version >= 0x3d0) writer.Write((byte)0);
            if (version >= 0x3e9) writer.Write((byte)0);
            if (version >= 0x3d1) writer.Write((byte)1);
            if (version >= 0x3d2) writer.Write((byte)0);

            if (info.GameVersion != 3) return;

            if (subversion >= 0x12)
            {
                writer.Write((byte)(info.LevelType == 6 ? 1 : (info.LevelType == 7 ? 2 : 0)));
            }
            if (subversion >= 0xd2) writer.Write((byte)0);
            if (subversion >= 0x11b)
            {
                WriteWStr(writer, "");
                writer.WriteUInt32BE(0); writer.WriteUInt32BE(0);
            }
            if (subversion >= 0x153) writer.Write((byte)1);
            if (subversion >= 0x192)
            {
                WriteStr(writer, "");
                if (subversion >= 0x206) WriteStr(writer, "");
            }
        }

        private static void WriteResDescriptor(BinaryWriter writer, uint revVersion, List<Tuple<string, uint>> deps, string? hashOrGuid, uint resrcType, bool isGuid = false)
        {
            byte hashType = 1; byte guidType = 2;
            if (revVersion < 0x191) { hashType = 2; guidType = 1; }

            if (string.IsNullOrEmpty(hashOrGuid))
            {
                writer.Write((byte)0);
            }
            else if (!isGuid)
            {
                writer.Write(hashType);
                writer.Write(StringToByteArray(hashOrGuid));
                deps.Add(Tuple.Create(hashOrGuid.ToLowerInvariant(), resrcType));
            }
            else
            {
                writer.Write(guidType);
                writer.WriteUInt32BE(uint.Parse(hashOrGuid, System.Globalization.NumberStyles.HexNumber));
                deps.Add(Tuple.Create(hashOrGuid, resrcType));
            }
        }

        private static void WriteOnlineId(BinaryWriter writer, uint revVersion, string npHandle)
        {
            bool lengthPrefixed = revVersion < 0x234;
            if (lengthPrefixed) writer.WriteUInt32BE(16);
            byte[] data = new byte[16];
            if (!string.IsNullOrEmpty(npHandle))
            {
                byte[] handleBytes = Encoding.UTF8.GetBytes(npHandle);
                Array.Copy(handleBytes, data, Math.Min(handleBytes.Length, 16));
            }
            writer.Write(data);
            writer.Write((byte)0);
            if (lengthPrefixed) writer.WriteUInt32BE(3);
            writer.Write(new byte[] { 0, 0, 0 });
        }

        #endregion

        #region Utilities

        private static uint ReadUInt32BE(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
        }

        private static void WriteWStr(BinaryWriter writer, string str)
        {
            byte[] utf16 = Encoding.BigEndianUnicode.GetBytes(str);
            writer.WriteUInt32BE((uint)(utf16.Length / 2));
            writer.Write(utf16);
        }

        private static void WriteStr(BinaryWriter writer, string str)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(str);
            writer.WriteUInt32BE((uint)utf8.Length);
            writer.Write(utf8);
        }

        private static byte[] TruncateStr(string s, int maxLen)
        {
            if (s == null) s = "";
            byte[] b = Encoding.UTF8.GetBytes(s);
            if (b.Length >= maxLen)
            {
                var res = new byte[maxLen];
                Array.Copy(b, res, maxLen - 4);
                res[maxLen - 4] = (byte)'.'; res[maxLen - 3] = (byte)'.'; res[maxLen - 2] = (byte)'.'; res[maxLen - 1] = 0;
                return res;
            }
            else
            {
                var res = new byte[b.Length + 1];
                Array.Copy(b, res, b.Length);
                res[b.Length] = 0;
                return res;
            }
        }

        public static byte[] StringToByteArray(string hex)
        {
            return Convert.FromHexString(hex);
        }

        private static void CreatePlaceholderIcon(string path)
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using Stream? stream = assembly.GetManifestResourceStream("LbpArchiveToolkit.Assets.MissingIcon.png");
                
                if (stream != null)
                {
                    using var fs = File.Create(path);
                    stream.CopyTo(fs);
                    return;
                }
            }
            catch { }

            // Pure WPF fallback if MissingIcon.png is lost
            int width = 320;
            int height = 176;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            
            // Fill with DarkGray #A9A9A9
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 169;     // B
                pixels[i + 1] = 169; // G
                pixels[i + 2] = 169; // R
                pixels[i + 3] = 255; // A
            }

            var source = System.Windows.Media.Imaging.BitmapSource.Create(
                width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
            source.Freeze();

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
            
            using var fileStream = File.Create(path);
            encoder.Save(fileStream);
        }

        #endregion
    }
}