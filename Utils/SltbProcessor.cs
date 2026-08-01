using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Buffers.Binary;
using LbpArchiveToolkit.Models;

namespace LbpArchiveToolkit.Utils
{
    public static class LbpConstants
    {
        // Revisions
        public const uint REV_DEPENDENCIES = 0x109;
        public const uint REV_SLOT_GROUPS = 0x134;
        public const uint REV_SLOT_AUTHOR_NAME = 0x13b;
        public const uint REV_COMPRESSED_RESOURCES = 0x189;
        public const uint REV_GUID_HASH_FLAGS_SWAP = 0x191;
        public const uint REV_NETWORK_ONLINE_ID = 0x234;
        public const uint REV_SLOT_DESCRIPTOR = 0x238;
        public const uint REV_BRANCHES = 0x271;
        public const uint REV_LBP1_MAX = 0x272;
        public const uint REV_ARCADE = 0x273;
        public const uint REV_SWITCHINPUT_VISIBILITY = 0x297;
        public const uint REV_SWITCH_BEHAVIOR = 0x2c4;
        public const uint REV_SLOT_COLLECTABUBBLES_REQUIRED = 0x2ea;
        public const uint REV_SLOT_COLLECTABUBBLES_CONTAINED = 0x2f4;
        public const uint REV_PLANET_DECORATIONS = 0x333;
        public const uint REV_SLOT_LABELS = 0x33c;
        public const uint REV_SLOT_SUBLEVEL = 0x352;
        public const uint REV_PRODUCTION_BUILD = 0x3b6;
        public const uint REV_SLOT_EXTRA_METADATA = 0x3d0;
        public const uint REV_SLOT_CROSS_COMPATIBLE = 0x3e9;

        // Leerdammer
        public const ushort BRANCH_LD_ID = 0x4c44;
        public const ushort REV_LD_RESOURCES = 0x02;

        // Subversions (LBP3)
        public const uint SUBREV_SLOT_GAME_MODE = 0x12;
        public const uint SUBREV_SLOT_GAME_KIT = 0xd2;
        public const uint SUBREV_SLOT_ENTRANCE_DATA = 0x11b;
        public const uint SUBREV_ADVENTURE = 0x145;
        public const uint SUBREV_SLOT_BADGE_SIZE = 0x153;
        public const uint SUBREV_SLOT_TRAILER_PATH = 0x192;
        public const uint SUBREV_SLOT_TRAILER_THUMBNAIL = 0x206;
        public const uint SUBREV_SLOT_ENFORCE_MINMAX = 0x215;

        // Default heads
        public const uint HEAD_LBP2_BETA_FALLBACK = 0x3b6;
        public const uint HEAD_LBP3_BASE = 0x010503e2;

        // Resource Types
        public const uint RES_TYPE_TEXTURE = 1;
        public const uint RES_TYPE_LEVEL = 9;
        public const uint RES_TYPE_ADVENTURE = 31;
        public const uint RES_TYPE_PLAN = 38;
    }

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
                    if (head >= LbpConstants.REV_DEPENDENCIES)
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
            if (!magic.AsSpan(0, 3).SequenceEqual("SLT"u8)) return sltData;

            uint head = BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
            uint depOffset = BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));

            ushort branchId = 0;
            ushort branchRev = 0;
            if (head >= LbpConstants.REV_BRANCHES)
            {
                branchId = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
                branchRev = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
            }

            byte compFlags = 0;
            bool hasCompFlags = head >= LbpConstants.REV_SWITCHINPUT_VISIBILITY || (head == LbpConstants.REV_LBP1_MAX && branchId == LbpConstants.BRANCH_LD_ID && branchRev >= LbpConstants.REV_LD_RESOURCES);
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
                    byte[] deflatedData = System.Buffers.ArrayPool<byte>.Shared.Rent(info.comp);
                    try
                    {
                        int compBytesRead = br.Read(deflatedData, 0, info.comp);
                        if (compBytesRead != info.comp) throw new EndOfStreamException();

                        using var msIn = new MemoryStream(deflatedData, 0, info.comp);
                        using var zlib = new ZLibStream(msIn, CompressionMode.Decompress);
                        int bytesRead = 0;
                        while (bytesRead < info.decomp)
                        {
                            int r = zlib.Read(decompressedPayload, currentPos + bytesRead, info.decomp - bytesRead);
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

            using var outMs = new MemoryStream();
            using var outW = new BinaryWriter(outMs);
            outW.Write("SLTb"u8);
            outW.WriteUInt32BE(head);

            long depOffsetPositionInHeader = outMs.Position;
            outW.WriteUInt32BE(0); 

            if (head >= 0x271) { outW.WriteUInt16BE(branchId); outW.WriteUInt16BE(branchRev); }
            if (hasCompFlags) outW.Write(compFlags);

            outW.Write((byte)0); 
            outW.Write(decompressedPayload);

            long finalDepOffset = outMs.Position;
            if (depOffset > 0 && depOffset < sltData.Length)
            {
                outW.Write(sltData.AsSpan((int)depOffset));
            }

            outMs.Position = depOffsetPositionInHeader;
            outW.WriteUInt32BE((uint)finalDepOffset); 

            return outMs.ToArray();
        }

         public static byte[] ReplaceHash(byte[] source, byte[] oldHash, byte[] newHash)
        {
            if (source.AsSpan().IndexOf(oldHash) == -1) return source; // NO-OP Check

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

        public static (bool isLocked, bool isSubLevel, bool isShareable) ReadSlotBools(byte[] sltData)
        {
            try {
                int offset = 4; // Skip "SLTb"
                uint head = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(sltData.AsSpan(offset, 4));
                offset += 4;
                uint version = head & 0xFFFF;
                uint subversion = (head >> 16) & 0xFFFF;
                
                if (head >= LbpConstants.REV_DEPENDENCIES) {
                    offset += 4;
                    if (head >= LbpConstants.REV_COMPRESSED_RESOURCES) {
                        ushort bId = 0, bRev = 0;
                        if (head >= LbpConstants.REV_BRANCHES) { 
                            bId = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(sltData.AsSpan(offset, 2)); 
                            offset += 2;
                            bRev = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(sltData.AsSpan(offset, 2)); 
                            offset += 2;
                        }
                        bool useCompressedInts = false;
                        if (head >= LbpConstants.REV_SWITCHINPUT_VISIBILITY || (head == LbpConstants.REV_LBP1_MAX && bId == LbpConstants.BRANCH_LD_ID && bRev >= LbpConstants.REV_LD_RESOURCES)) { 
                            useCompressedInts = (sltData[offset++] & 1) != 0; 
                        }
                        offset += 1;
                        
                        long ReadUleb128() {
                            long result = 0; int shift = 0;
                            while (true) {
                                byte b = sltData[offset++];
                                result |= (long)(b & 0x7F) << shift;
                                if ((b & 0x80) == 0) break;
                                shift += 7;
                            }
                            return result;
                        }
                        int ReadI32() {
                            if (useCompressedInts) return (int)(ReadUleb128() & 0xFFFFFFFF);
                            int res = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(sltData.AsSpan(offset, 4));
                            offset += 4;
                            return res;
                        }
                        long ReadU32() {
                            if (useCompressedInts) return ReadUleb128();
                            long res = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(sltData.AsSpan(offset, 4));
                            offset += 4;
                            return res;
                        }
                        int ReadS32() {
                            if (useCompressedInts) { uint v = (uint)ReadUleb128(); return (int)((v >> 1) ^ -(int)(v & 1)); }
                            int res = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(sltData.AsSpan(offset, 4));
                            offset += 4;
                            return res;
                        }
                        
                        int slotCount = ReadI32();
                        int slotType = ReadI32();
                        long slotNumber = ReadU32();
                        
                        void SkipResDescriptor() {
                            byte flags = sltData[offset++];
                            if (flags == 0) return;
                            if ((flags & (version < LbpConstants.REV_GUID_HASH_FLAGS_SWAP ? (byte)1 : (byte)2)) != 0) ReadU32();
                            if ((flags & (version < LbpConstants.REV_GUID_HASH_FLAGS_SWAP ? (byte)2 : (byte)1)) != 0) offset += 20;
                        }

                        SkipResDescriptor();
                        if (subversion >= LbpConstants.SUBREV_ADVENTURE) SkipResDescriptor();
                        SkipResDescriptor();
                        
                        offset += 16;
                        bool lengthPrefixed = version < LbpConstants.REV_NETWORK_ONLINE_ID;
                        if (lengthPrefixed) ReadI32();
                        offset += 16; // npData
                        offset += 1;
                        if (lengthPrefixed) ReadI32();
                        offset += 3;
                        
                        if (version >= LbpConstants.REV_SLOT_AUTHOR_NAME) { int authorLen = ReadS32(); offset += authorLen * 2; }
                        int transLen = ReadS32(); offset += transLen;
                        
                        int nameLen = ReadS32(); offset += nameLen * 2;
                        int descLen = ReadS32(); offset += descLen * 2;
                        
                        ReadI32(); ReadI32(); // 0, 0
                        if (version >= LbpConstants.REV_SLOT_GROUPS) { ReadI32(); ReadI32(); }
                        
                        bool isLocked = sltData[offset++] != 0;
                        bool isShareable = true;
                        if (version >= LbpConstants.REV_SLOT_DESCRIPTOR) {
                            isShareable = sltData[offset++] != 0;
                            ReadI32(); // bg guid
                        }
                        
                        if (version > LbpConstants.REV_PLANET_DECORATIONS) SkipResDescriptor();
                        if (version < 0x188) offset++;
                        if (version > 0x1de) ReadI32();
                        if (version > 0x1ad && version < 0x1b9) offset++;
                        if (version > 0x1b8 && version < 0x36c) ReadI32();
                        
                        bool isSubLevel = false;
                        if (version >= LbpConstants.REV_SWITCH_BEHAVIOR) {
                            if (version >= LbpConstants.REV_SLOT_LABELS) {
                                int lblCount = ReadI32();
                                for (int i = 0; i < lblCount; i++) { ReadI32(); ReadI32(); }
                            }
                            if (version >= LbpConstants.REV_SLOT_COLLECTABUBBLES_REQUIRED) {
                                ReadI32();
                                for (int i = 0; i < 3; i++) { SkipResDescriptor(); ReadI32(); }
                            }
                            if (version >= LbpConstants.REV_SLOT_COLLECTABUBBLES_CONTAINED) ReadI32();
                            if (version >= LbpConstants.REV_SLOT_SUBLEVEL) {
                                isSubLevel = sltData[offset++] != 0;
                            }
                        }
                        
                        return (isLocked, isSubLevel, isShareable);
                    }
                }
            } catch { }
            return (false, false, true);
        }

        public static (byte[] patchedSlt, string npHandle, int gameVersion) PatchSltb(byte[] sltData, string newName, string newDesc, byte[]? newIconHash = null, bool? isLocked = null, bool? isSubLevel = null, bool? isShareable = null)
        {
            using var ms = new MemoryStream(sltData);
            using var br = new BinaryReader(ms);

            br.ReadBytes(4);
            uint head = BinaryPrimitives.ReadUInt32BigEndian(br.ReadBytes(4));
            uint version = head & 0xFFFF;
            uint subversion = (head >> 16) & 0xFFFF;
            int gameVersion = subversion != 0 ? 3 : (version >= LbpConstants.REV_ARCADE ? 2 : 1);

            int depOffsetPos = -1;
            bool useCompressedInts = false;

            if (head >= LbpConstants.REV_DEPENDENCIES)
            {
                depOffsetPos = (int)ms.Position;
                ms.Position += 4; 
                if (head >= LbpConstants.REV_COMPRESSED_RESOURCES)
                {
                    ushort bId = 0, bRev = 0;
                    if (head >= LbpConstants.REV_BRANCHES) { bId = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2)); bRev = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2)); }
                    if (head >= LbpConstants.REV_SWITCHINPUT_VISIBILITY || (head == LbpConstants.REV_LBP1_MAX && bId == LbpConstants.BRANCH_LD_ID && bRev >= LbpConstants.REV_LD_RESOURCES)) { useCompressedInts = (br.ReadByte() & 1) != 0; }
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
                if ((flags & (version < LbpConstants.REV_GUID_HASH_FLAGS_SWAP ? (byte)1 : (byte)2)) != 0) ReadU32();
                if ((flags & (version < LbpConstants.REV_GUID_HASH_FLAGS_SWAP ? (byte)2 : (byte)1)) != 0)
                {
                    if (isIcon) oldIconHash = br.ReadBytes(20);
                    else ms.Position += 20; 
                }
            }

            SkipResDescriptor();
            if (subversion >= LbpConstants.SUBREV_ADVENTURE) SkipResDescriptor(); 

            int iconDescStart = (int)ms.Position;
            SkipResDescriptor(true); 
            int iconDescEnd = (int)ms.Position;

            ms.Position += 16; 

            bool lengthPrefixed = version < LbpConstants.REV_NETWORK_ONLINE_ID;
            if (lengthPrefixed) ReadI32();
            byte[] npData = br.ReadBytes(16);
            ms.Position += 1;
            if (lengthPrefixed) ReadI32();
            ms.Position += 3;

            int len = Array.IndexOf(npData, (byte)0);
            string npHandle = Encoding.UTF8.GetString(npData, 0, len < 0 ? 16 : len);

            if (version >= LbpConstants.REV_SLOT_AUTHOR_NAME) { int authorLen = ReadS32(); ms.Position += authorLen * 2; }
            int transLen = ReadS32(); ms.Position += transLen;

            int stringsOffset = (int)ms.Position;
            int nameLen = ReadS32(); ms.Position += nameLen * 2;
            int descLen = ReadS32(); ms.Position += descLen * 2;
            int endOfStrings = (int)ms.Position;

            byte[] nameBytes = Encoding.BigEndianUnicode.GetBytes(newName);
            byte[] descBytes = Encoding.BigEndianUnicode.GetBytes(newDesc);

            using var patchedMs = new MemoryStream();

            int currentOffset = endOfStrings;
            
            void SkipResDescriptorBytes(ref int offset) {
                byte flags = sltData[offset++];
                if (flags == 0) return;
                if ((flags & (version < LbpConstants.REV_GUID_HASH_FLAGS_SWAP ? (byte)1 : (byte)2)) != 0) {
                    if (useCompressedInts) {
                        while ((sltData[offset++] & 0x80) != 0) {}
                    } else offset += 4;
                }
                if ((flags & (version < LbpConstants.REV_GUID_HASH_FLAGS_SWAP ? (byte)2 : (byte)1)) != 0) offset += 20;
            }
            
            void SkipI32Bytes(ref int offset) {
                if (useCompressedInts) {
                    while ((sltData[offset++] & 0x80) != 0) {}
                } else offset += 4;
            }

            if (isLocked.HasValue || isSubLevel.HasValue || isShareable.HasValue) {
                try {
                    SkipI32Bytes(ref currentOffset); SkipI32Bytes(ref currentOffset); // 0, 0
                    if (version >= LbpConstants.REV_SLOT_GROUPS) { SkipI32Bytes(ref currentOffset); SkipI32Bytes(ref currentOffset); }
                    
                    if (isLocked.HasValue) sltData[currentOffset] = (byte)(isLocked.Value ? 1 : 0);
                    currentOffset += 1;
                    
                    if (version >= LbpConstants.REV_SLOT_DESCRIPTOR) {
                        if (isShareable.HasValue) sltData[currentOffset] = (byte)(isShareable.Value ? 1 : 0);
                        currentOffset += 1;
                        SkipI32Bytes(ref currentOffset); // bg guid
                    }
                    
                    if (isSubLevel.HasValue) {
                        if (version > LbpConstants.REV_PLANET_DECORATIONS) SkipResDescriptorBytes(ref currentOffset);
                        if (version < 0x188) currentOffset += 1;
                        if (version > 0x1de) SkipI32Bytes(ref currentOffset);
                        if (version > 0x1ad && version < 0x1b9) currentOffset += 1;
                        if (version > 0x1b8 && version < 0x36c) SkipI32Bytes(ref currentOffset);
                        
                        if (version >= LbpConstants.REV_SWITCH_BEHAVIOR) {
                            if (version >= LbpConstants.REV_SLOT_LABELS) {
                                int prevPos = (int)ms.Position;
                                ms.Position = currentOffset;
                                int lblCount = ReadI32();
                                currentOffset = (int)ms.Position;
                                for (int i = 0; i < lblCount; i++) { SkipI32Bytes(ref currentOffset); SkipI32Bytes(ref currentOffset); }
                            }
                            if (version >= LbpConstants.REV_SLOT_COLLECTABUBBLES_REQUIRED) {
                                SkipI32Bytes(ref currentOffset);
                                for (int i = 0; i < 3; i++) { SkipResDescriptorBytes(ref currentOffset); SkipI32Bytes(ref currentOffset); }
                            }
                            if (version >= LbpConstants.REV_SLOT_COLLECTABUBBLES_CONTAINED) SkipI32Bytes(ref currentOffset);
                            if (version >= LbpConstants.REV_SLOT_SUBLEVEL) {
                                sltData[currentOffset] = (byte)(isSubLevel.Value ? 1 : 0);
                            }
                        }
                    }
                } catch { }
            }

            if (newIconHash != null && oldIconHash == null)
            {
                patchedMs.Write(sltData, 0, iconDescStart);
                patchedMs.WriteByte(version < LbpConstants.REV_GUID_HASH_FLAGS_SWAP ? (byte)2 : (byte)1);
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

                if (resrcType != "SMH" && head >= LbpConstants.REV_BRANCHES)
                {
                    int offset = head >= LbpConstants.REV_DEPENDENCIES ? 12 : 8;
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
            if (head >= LbpConstants.REV_DEPENDENCIES)
            {
                w.WriteUInt32BE(0);
                if (head >= LbpConstants.REV_COMPRESSED_RESOURCES)
                {
                    if (head >= LbpConstants.REV_BRANCHES) { w.WriteUInt16BE(branchId); w.WriteUInt16BE(branchRev); }
                    if (head >= LbpConstants.REV_SWITCHINPUT_VISIBILITY || (head == LbpConstants.REV_LBP1_MAX && branchId == LbpConstants.BRANCH_LD_ID && branchRev >= LbpConstants.REV_LD_RESOURCES)) w.Write((byte)0);
                    w.Write((byte)0);
                }
            }
            w.WriteUInt32BE(1);

            var deps = new List<Tuple<string, uint>>();
            WriteSlotStruct(w, head & 0xFFFF, (head >> 16) & 0xFFFF, info, deps);

            if ((head & 0xFFFF) >= LbpConstants.REV_PRODUCTION_BUILD) w.Write((byte)1);

            if (head >= LbpConstants.REV_DEPENDENCIES)
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
            WriteResDescriptor(writer, version, deps, info.IsAdventurePlanet ? null : info.RootLevelHash, LbpConstants.RES_TYPE_LEVEL, isRootGuid);
            if (subversion >= LbpConstants.SUBREV_ADVENTURE) WriteResDescriptor(writer, version, deps, info.IsAdventurePlanet ? info.RootLevelHash : null, LbpConstants.RES_TYPE_ADVENTURE, isRootGuid);
            
            bool isIconGuid = !string.IsNullOrEmpty(info.IconHash) && info.IconHash.Length <= 8;
            WriteResDescriptor(writer, version, deps, info.IconHash, LbpConstants.RES_TYPE_TEXTURE, isIconGuid);

            for (int i = 0; i < 4; i++) writer.WriteUInt32BE(0);

            WriteOnlineId(writer, version, info.NpHandle);
            if (version >= LbpConstants.REV_SLOT_AUTHOR_NAME) WriteWStr(writer, info.NpHandle);
            WriteStr(writer, "");
            WriteWStr(writer, info.Name);
            WriteWStr(writer, info.Description);

            writer.WriteUInt32BE(0); writer.WriteUInt32BE(0);
            if (version >= LbpConstants.REV_SLOT_GROUPS) { writer.WriteUInt32BE(0); writer.WriteUInt32BE(0); }
            writer.Write((byte)(info.InitiallyLocked ? 1 : 0));

            if (version >= LbpConstants.REV_SLOT_DESCRIPTOR) { writer.Write((byte)(info.Shareable ? 1 : 0)); writer.WriteUInt32BE(info.BackgroundGuid); }
            if (version > LbpConstants.REV_PLANET_DECORATIONS) WriteResDescriptor(writer, version, deps, null, LbpConstants.RES_TYPE_PLAN, false);
            if (version < 0x188) writer.Write((byte)0); // Unknown removed field
            
            if (version > 0x1de) writer.WriteUInt32BE(info.LevelType == 6 ? 6u : (info.LevelType == 7 ? 7u : 0u)); // WORLD_TUTORIAL_LEVEL = 0x1de
            else writer.Write((byte)0);

            if (version > 0x1ad && version < 0x1b9) writer.Write((byte)0);
            if (version > 0x1b8 && version < 0x36c) writer.WriteUInt32BE(0);
            if (version < LbpConstants.REV_SWITCH_BEHAVIOR) return;

            if (version >= LbpConstants.REV_SLOT_LABELS)
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

            if (version >= LbpConstants.REV_SLOT_COLLECTABUBBLES_REQUIRED)
            {
                writer.WriteUInt32BE(3);
                for (int i = 0; i < 3; i++) { WriteResDescriptor(writer, version, deps, null, LbpConstants.RES_TYPE_PLAN, false); writer.WriteUInt32BE(0); }
            }

            if (version >= LbpConstants.REV_SLOT_COLLECTABUBBLES_CONTAINED) writer.WriteUInt32BE(0);
            if (version >= LbpConstants.REV_SLOT_SUBLEVEL) writer.Write((byte)(info.IsSubLevel ? 1 : 0));
            if (version < LbpConstants.REV_SLOT_EXTRA_METADATA) return;

            writer.Write((byte)info.MinPlayers);
            writer.Write((byte)info.MaxPlayers);
            if (subversion >= LbpConstants.SUBREV_SLOT_ENFORCE_MINMAX) writer.Write((byte)0);
            if (version >= LbpConstants.REV_SLOT_EXTRA_METADATA) writer.Write((byte)0);
            if (version >= LbpConstants.REV_SLOT_CROSS_COMPATIBLE) writer.Write((byte)0);
            if (version >= 0x3d1) writer.Write((byte)1);
            if (version >= 0x3d2) writer.Write((byte)0);

            if (info.GameVersion != 3) return;
            if (subversion >= LbpConstants.SUBREV_SLOT_GAME_MODE) writer.Write((byte)(info.LevelType == 6 ? 1 : (info.LevelType == 7 ? 2 : 0)));
            if (subversion >= LbpConstants.SUBREV_SLOT_GAME_KIT) writer.Write((byte)0);
            if (subversion >= LbpConstants.SUBREV_SLOT_ENTRANCE_DATA) { WriteWStr(writer, ""); writer.WriteUInt32BE(0); writer.WriteUInt32BE(0); }
            if (subversion >= LbpConstants.SUBREV_SLOT_BADGE_SIZE) writer.Write((byte)1);
            if (subversion >= LbpConstants.SUBREV_SLOT_TRAILER_PATH) { WriteStr(writer, ""); if (subversion >= LbpConstants.SUBREV_SLOT_TRAILER_THUMBNAIL) WriteStr(writer, ""); }
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