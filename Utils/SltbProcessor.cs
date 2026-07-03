using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Buffers.Binary;
using LbpArchiveToolkit.Models;

namespace LbpArchiveToolkit.Utils
{
    public static class SltbProcessor
    {
        public static List<string> GetDependenciesFast(byte[] data)
        {
            var deps = new List<string>();
            if (data.Length < 12) return deps;

            byte method = data[3];
            byte[] workData = data;
            byte[]? rentedBuffer = null;
            int headOffset = 4;
            int tableOffsetPos = 8;

            try
            {
                if (method == (byte)'e')
                {
                    uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4));
                    if (8L + size > data.Length) return deps;

                    rentedBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent((int)size);
                    workData = rentedBuffer;
                    Array.Copy(data, 8, workData, 0, (int)size);
                    Far4Crypto.XxteaDecrypt(workData, (int)size);

                    headOffset = 0;
                    tableOffsetPos = 4;
                }

                if (method == (byte)'b' || method == (byte)'e')
                {
                    uint head = BinaryPrimitives.ReadUInt32BigEndian(workData.AsSpan(headOffset, 4));
                    if (head >= 0x109)
                    {
                        uint tableOffset = BinaryPrimitives.ReadUInt32BigEndian(workData.AsSpan(tableOffsetPos, 4));
                        if (tableOffset < workData.Length)
                        {
                            int ptr = (int)tableOffset;
                            if (ptr + 4 > workData.Length) return deps;

                            uint count = BinaryPrimitives.ReadUInt32BigEndian(workData.AsSpan(ptr, 4));
                            ptr += 4;

                            long maxPossibleDeps = (workData.Length - ptr) / 4;
                            uint safeCount = (uint)Math.Min(count, maxPossibleDeps);

                            deps.Capacity = (int)safeCount;

                            for (int i = 0; i < safeCount; i++)
                            {
                                if (ptr >= workData.Length) break;
                                byte flags = workData[ptr++];

                                if ((flags & 2) != 0)
                                {
                                    if (ptr + 4 > workData.Length) break;
                                    ptr += 4;
                                }

                                if ((flags & 1) != 0)
                                {
                                    if (ptr + 20 > workData.Length) break;
                                    string hashStr = Convert.ToHexStringLower(workData[ptr..(ptr + 20)]);
                                    deps.Add(hashStr);
                                    ptr += 20;
                                }

                                if (ptr + 4 > workData.Length) break;
                                ptr += 4;
                            }
                        }
                    }
                }
            }
            finally
            {
                if (rentedBuffer != null) System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
            return deps;
        }

        public static byte[]? DecompressSltData(byte[]? sltData)
        {
            if (sltData == null || sltData.Length < 12) return sltData;

            using var ms = new MemoryStream(sltData);
            using var br = new BinaryReader(ms);

            byte[] magic = br.ReadBytes(4); 
            string magicStr = Encoding.ASCII.GetString(magic, 0, 3);
            if (magicStr != "SLT") return sltData;

            uint head = BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
            uint depOffset = BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));

            ushort branchId = 0;
            ushort branchRev = 0;
            if (head >= 0x271)
            {
                branchId = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
                branchRev = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
            }

            byte compFlags = 0;
            bool hasCompFlags = head >= 0x297 || (head == 0x272 && branchId == 0x4c44 && branchRev >= 2);
            if (hasCompFlags) compFlags = br.ReadByte();

            bool isCompressed = br.ReadByte() != 0;
            if (!isCompressed) return sltData;

            ushort flag = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2)); 
            ushort numChunks = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));

            var chunkInfos = new List<(ushort comp, ushort decomp)>();
            int totalDecompSize = 0;
            for (int i = 0; i < numChunks; i++)
            {
                ushort compSize = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
                ushort decompSize = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
                chunkInfos.Add((compSize, decompSize));
                totalDecompSize += decompSize;
            }

            byte[] decompressedPayload = new byte[totalDecompSize];
            int currentPos = 0;
            for (int i = 0; i < numChunks; i++)
            {
                var info = chunkInfos[i];
                if (info.comp == info.decomp)
                {
                    int uncompBytesRead = br.Read(decompressedPayload, currentPos, info.comp);
                    if (uncompBytesRead != info.comp) throw new EndOfStreamException();
                }
                else
                {
                    byte[] deflatedData = br.ReadBytes(info.comp);
                    using var msIn = new MemoryStream(deflatedData);
                    using var zlib = new ZLibStream(msIn, CompressionMode.Decompress);
                    int bytesRead = 0;
                    while (bytesRead < info.decomp)
                    {
                        int r = zlib.Read(decompressedPayload, currentPos + bytesRead, info.decomp - bytesRead);
                        if (r == 0) break;
                        bytesRead += r;
                    }
                }
                currentPos += info.decomp;
            }

            byte[] depTableBytes = Array.Empty<byte>();
            if (depOffset > 0 && depOffset < sltData.Length)
            {
                depTableBytes = new byte[sltData.Length - depOffset];
                Array.Copy(sltData, depOffset, depTableBytes, 0, depTableBytes.Length);
            }

            using var outMs = new MemoryStream();
            using var outW = new BinaryWriter(outMs);
            outW.Write(Encoding.ASCII.GetBytes("SLTb"));
            outW.WriteUInt32BE(head);

            long depOffsetPositionInHeader = outMs.Position;
            outW.WriteUInt32BE(0); 

            if (head >= 0x271) { outW.WriteUInt16BE(branchId); outW.WriteUInt16BE(branchRev); }
            if (hasCompFlags) outW.Write(compFlags);

            outW.Write((byte)0); 
            outW.Write(decompressedPayload);

            long finalDepOffset = outMs.Position;
            outW.Write(depTableBytes);

            outMs.Position = depOffsetPositionInHeader;
            outW.WriteUInt32BE((uint)finalDepOffset); 

            return outMs.ToArray();
        }

        public static byte[] ReplaceHash(byte[] source, byte[] oldHash, byte[] newHash)
        {
            byte[] result = new byte[source.Length];
            Array.Copy(source, result, source.Length);

            Span<byte> searchSpan = result;
            int index;
            
            while ((index = searchSpan.IndexOf(oldHash)) != -1)
            {
                newHash.CopyTo(searchSpan.Slice(index, 20));
                searchSpan = searchSpan.Slice(index + 20);
            }
            return result;
        }

        public static (byte[] patchedSlt, string npHandle, int gameVersion) PatchSltb(byte[] sltData, string newName, string newDesc, byte[]? newIconHash = null)
        {
            using var ms = new MemoryStream(sltData);
            using var br = new BinaryReader(ms);

            br.ReadBytes(4);
            uint head = BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
            uint version = head & 0xFFFF;
            uint subversion = (head >> 16) & 0xFFFF;
            int gameVersion = version >= 0x3d0 ? 3 : (version >= 0x273 ? 2 : 1);

            int depOffsetPos = -1;
            bool useCompressedInts = false;

            if (head >= 0x109)
            {
                depOffsetPos = (int)ms.Position;
                ms.Position += 4; 
                if (head >= 0x189)
                {
                    ushort bId = 0, bRev = 0;
                    if (head >= 0x271) { bId = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2)); bRev = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2)); }
                    if (head >= 0x297 || (head == 0x272 && bId == 0x4c44 && bRev >= 2)) { useCompressedInts = (br.ReadByte() & 1) != 0; }
                    ms.Position += 1; 
                }
            }

            long ReadUleb128()
            {
                long result = 0; int shift = 0;
                while (true)
                {
                    byte b = br.ReadByte();
                    result |= (long)(b & 0x7F) << shift;
                    if ((b & 0x80) == 0) break;
                    shift += 7;
                }
                return result;
            }

            int ReadI32() => useCompressedInts ? (int)(ReadUleb128() & 0xFFFFFFFF) : (int)BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
            long ReadU32() => useCompressedInts ? ReadUleb128() : (long)BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
            int ReadS32()
            {
                if (useCompressedInts) { uint v = (uint)ReadUleb128(); return (int)((v >> 1) ^ -(int)(v & 1)); }
                return (int)BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
            }

            void WriteUleb128(MemoryStream outStream, long value)
            {
                ulong v = (ulong)value;
                do { byte b = (byte)(v & 0x7F); v >>= 7; if (v != 0) b |= 0x80; outStream.WriteByte(b); } while (v != 0);
            }

            void WriteS32(MemoryStream outStream, int value)
            {
                if (useCompressedInts) { WriteUleb128(outStream, (uint)((value << 1) ^ (value >> 31))); }
                else { Span<byte> buf = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(buf, value); outStream.Write(buf); }
            }

            int slotCount = ReadI32();
            int slotType = ReadI32();
            long slotNumber = ReadU32();

            byte[]? oldIconHash = null;
            void SkipResDescriptor(bool isIcon = false)
            {
                byte flags = br.ReadByte();
                if (flags == 0) return;
                if ((flags & (version < 0x191 ? (byte)1 : (byte)2)) != 0) ReadU32();
                if ((flags & (version < 0x191 ? (byte)2 : (byte)1)) != 0)
                {
                    if (isIcon) oldIconHash = br.ReadBytes(20);
                    else ms.Position += 20; 
                }
            }

            SkipResDescriptor();
            if (subversion >= 0x145) SkipResDescriptor(); 

            int iconDescStart = (int)ms.Position;
            SkipResDescriptor(true); 
            int iconDescEnd = (int)ms.Position;

            ms.Position += 16; 

            bool lengthPrefixed = version < 0x234;
            if (lengthPrefixed) ReadI32();
            byte[] npData = br.ReadBytes(16);
            ms.Position += 1;
            if (lengthPrefixed) ReadI32();
            ms.Position += 3;

            int len = Array.IndexOf(npData, (byte)0);
            string npHandle = Encoding.UTF8.GetString(npData, 0, len < 0 ? 16 : len);

            if (version >= 0x13b) { int authorLen = ReadS32(); ms.Position += authorLen * 2; }
            int transLen = ReadS32(); ms.Position += transLen;

            int stringsOffset = (int)ms.Position;
            int nameLen = ReadS32(); ms.Position += nameLen * 2;
            int descLen = ReadS32(); ms.Position += descLen * 2;
            int endOfStrings = (int)ms.Position;

            byte[] nameBytes = Encoding.BigEndianUnicode.GetBytes(newName);
            byte[] descBytes = Encoding.BigEndianUnicode.GetBytes(newDesc);

            using var patchedMs = new MemoryStream();

            if (newIconHash != null && oldIconHash == null)
            {
                patchedMs.Write(sltData, 0, iconDescStart);
                patchedMs.WriteByte(version < 0x191 ? (byte)2 : (byte)1);
                patchedMs.Write(newIconHash, 0, 20);
                patchedMs.Write(sltData, iconDescEnd, stringsOffset - iconDescEnd);
            }
            else
            {
                patchedMs.Write(sltData, 0, stringsOffset);
            }

            WriteS32(patchedMs, nameBytes.Length / 2);
            patchedMs.Write(nameBytes, 0, nameBytes.Length);
            WriteS32(patchedMs, descBytes.Length / 2);
            patchedMs.Write(descBytes, 0, descBytes.Length);

            patchedMs.Write(sltData, endOfStrings, sltData.Length - endOfStrings);
            byte[] patched = patchedMs.ToArray();

            int depDelta = patched.Length - sltData.Length;

            if (newIconHash != null)
            {
                if (oldIconHash != null) patched = ReplaceHash(patched, oldIconHash, newIconHash);
                else if (depOffsetPos != -1)
                {
                    uint oldDepOffset = BinaryPrimitives.ReadUInt32BigEndian(sltData.AsSpan(depOffsetPos, 4));
                    int depTableStart = (int)oldDepOffset + depDelta;

                    uint depCount = BinaryPrimitives.ReadUInt32BigEndian(patched.AsSpan(depTableStart, 4));
                    BinaryPrimitives.WriteUInt32BigEndian(patched.AsSpan(depTableStart, 4), depCount + 1);

                    byte[] newDep = new byte[25];
                    newDep[0] = 1;
                    Array.Copy(newIconHash, 0, newDep, 1, 20);
                    BinaryPrimitives.WriteUInt32BigEndian(newDep.AsSpan(21, 4), 1);

                    byte[] finalPatched = new byte[patched.Length + 25];
                    Array.Copy(patched, finalPatched, patched.Length);
                    Array.Copy(newDep, 0, finalPatched, patched.Length, 25);
                    patched = finalPatched;
                }
            }

            if (depOffsetPos != -1)
            {
                uint oldDepOffset = BinaryPrimitives.ReadUInt32BigEndian(sltData.AsSpan(depOffsetPos, 4));
                BinaryPrimitives.WriteUInt32BigEndian(patched.AsSpan(depOffsetPos, 4), (uint)((int)oldDepOffset + depDelta));
            }

            return (patched, npHandle, gameVersion);
        }

        public static void ParseResrcRevision(byte[] rootLevelData, out uint head, out ushort branchId, out ushort branchRev)
        {
            head = 0; branchId = 0; branchRev = 0;
            if (rootLevelData == null || rootLevelData.Length < 8) return;

            byte method = rootLevelData[3];
            if (method == 'b' || method == 'e')
            {
                head = BinaryPrimitives.ReadUInt32BigEndian(rootLevelData[4..8]);
                string resrcType = Encoding.ASCII.GetString(rootLevelData, 0, 3);

                if (resrcType != "SMH" && head >= 0x271)
                {
                    int offset = head >= 0x109 ? 12 : 8;
                    if (rootLevelData.Length >= offset + 4)
                    {
                        branchId = BinaryPrimitives.ReadUInt16BigEndian(rootLevelData[offset..(offset + 2)]);
                        branchRev = BinaryPrimitives.ReadUInt16BigEndian(rootLevelData[(offset + 2)..(offset + 4)]);
                    }
                }
            }
        }

        public static byte[] MakeSlotList(uint head, ushort branchId, ushort branchRev, SlotInfo info)
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
                        w.Write((byte)1);
                        w.Write(Convert.FromHexString(dep.Item1));
                    }
                    else
                    {
                        w.Write((byte)2);
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
            WriteResDescriptor(writer, version, deps, info.IsAdventurePlanet ? null : info.RootLevelHash, 9, isRootGuid);
            if (subversion >= 0x145) WriteResDescriptor(writer, version, deps, info.IsAdventurePlanet ? info.RootLevelHash : null, 31, isRootGuid);
            
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

            if (version > 0x237) { writer.Write((byte)(info.Shareable ? 1 : 0)); writer.WriteUInt32BE(info.BackgroundGuid); }
            if (version > 0x333) WriteResDescriptor(writer, version, deps, null, 38, false);
            if (version < 0x188) writer.Write((byte)0);
            
            if (version > 0x1de) writer.WriteUInt32BE(info.LevelType == 6 ? 6u : (info.LevelType == 7 ? 7u : 0u));
            else writer.Write((byte)0);

            if (version > 0x1ad && version < 0x1b9) writer.Write((byte)0);
            if (version > 0x1b8 && version < 0x36c) writer.WriteUInt32BE(0);
            if (version <= 0x2c3) return;

            if (version >= 0x33c)
            {
                var labelsToWrite = info.Labels;
                if (info.GameVersion == 2)
                {
                    labelsToWrite = new List<uint>();
                    foreach (var l in info.Labels) if (LabelParser.IsLbp2Label(l)) labelsToWrite.Add(l);
                }
                writer.WriteUInt32BE((uint)labelsToWrite.Count);
                for (int i = 0; i < labelsToWrite.Count; i++) { writer.WriteUInt32BE(labelsToWrite[i]); writer.WriteUInt32BE((uint)i); }
            }

            if (version >= 0x2ea)
            {
                writer.WriteUInt32BE(3);
                for (int i = 0; i < 3; i++) { WriteResDescriptor(writer, version, deps, null, 38, false); writer.WriteUInt32BE(0); }
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
            if (subversion >= 0x12) writer.Write((byte)(info.LevelType == 6 ? 1 : (info.LevelType == 7 ? 2 : 0)));
            if (subversion >= 0xd2) writer.Write((byte)0);
            if (subversion >= 0x11b) { WriteWStr(writer, ""); writer.WriteUInt32BE(0); writer.WriteUInt32BE(0); }
            if (subversion >= 0x153) writer.Write((byte)1);
            if (subversion >= 0x192) { WriteStr(writer, ""); if (subversion >= 0x206) WriteStr(writer, ""); }
        }

        private static void WriteResDescriptor(BinaryWriter writer, uint revVersion, List<Tuple<string, uint>> deps, string? hashOrGuid, uint resrcType, bool isGuid = false)
        {
            if (string.IsNullOrEmpty(hashOrGuid)) writer.Write((byte)0);
            else if (!isGuid)
            {
                writer.Write(revVersion < 0x191 ? (byte)2 : (byte)1);
                writer.Write(Convert.FromHexString(hashOrGuid));
                deps.Add(Tuple.Create(hashOrGuid.ToLowerInvariant(), resrcType));
            }
            else
            {
                writer.Write(revVersion < 0x191 ? (byte)1 : (byte)2);
                writer.WriteUInt32BE(uint.Parse(hashOrGuid, System.Globalization.NumberStyles.HexNumber));
                deps.Add(Tuple.Create(hashOrGuid, resrcType));
            }
        }

        private static void WriteOnlineId(BinaryWriter writer, uint revVersion, string npHandle)
        {
            bool lengthPrefixed = revVersion < 0x234;
            if (lengthPrefixed) writer.WriteUInt32BE(16);
            byte[] data = new byte[16];
            if (!string.IsNullOrEmpty(npHandle)) Array.Copy(Encoding.UTF8.GetBytes(npHandle), data, Math.Min(Encoding.UTF8.GetBytes(npHandle).Length, 16));
            writer.Write(data); writer.Write((byte)0);
            if (lengthPrefixed) writer.WriteUInt32BE(3);
            writer.Write(new byte[] { 0, 0, 0 });
        }

        private static void WriteWStr(BinaryWriter writer, string str)
        {
            byte[] utf16 = Encoding.BigEndianUnicode.GetBytes(str);
            writer.WriteUInt32BE((uint)(utf16.Length / 2)); writer.Write(utf16);
        }

        private static void WriteStr(BinaryWriter writer, string str)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(str);
            writer.WriteUInt32BE((uint)utf8.Length); writer.Write(utf8);
        }
    }
}