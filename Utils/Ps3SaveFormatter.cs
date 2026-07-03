using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LbpArchiveToolkit.Utils
{
    public static class Ps3SaveFormatter
    {
        private static readonly byte[] SYSCON_MANAGER_KEY = new byte[] { 0xd4, 0x13, 0xb8, 0x96, 0x63, 0xe1, 0xfe, 0x9f, 0x75, 0x14, 0x3d, 0x3b, 0xb4, 0x56, 0x52, 0x74 };
        private static readonly byte[] KEYGEN_KEY = new byte[] { 0x6b, 0x1a, 0xce, 0xa2, 0x46, 0xb7, 0x45, 0xfd, 0x8f, 0x93, 0x76, 0x3b, 0x92, 0x05, 0x94, 0xcd, 0x53, 0x48, 0x3b, 0x82 };
        private static readonly byte[] SAVEGAME_PARAM_SFO_KEY = new byte[] { 0x0c, 0x08, 0x00, 0x0e, 0x09, 0x05, 0x04, 0x04, 0x0d, 0x01, 0x0f, 0x00, 0x04, 0x06, 0x02, 0x02, 0x09, 0x06, 0x0d, 0x03 };

        public static byte[] MakeSfo(string displayName, string bkpName, string npHandle, string description, int gameVersion)
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
                ("PARENTAL_LEVEL", 4, 4, new byte[] { 0, 0, 0, 0 }),
                ("SAVEDATA_DIRECTORY", 4, 64, TruncateStr(bkpName, 64)),
                ("SAVEDATA_LIST_PARAM", 4, 8, TruncateStr("", 8)),
                ("SUB_TITLE", 4, 128, TruncateStr(subtitle, 128)),
                ("TITLE", 4, 128, TruncateStr(title, 128))
            };

            var keyTable = new MemoryStream();
            int[] keyOffsets = new int[11];
            for (int i = 0; i < 11; i++)
            {
                keyOffsets[i] = (int)keyTable.Position;
                byte[] kb = Encoding.ASCII.GetBytes(entries[i].key);
                keyTable.Write(kb, 0, kb.Length);
                keyTable.WriteByte(0);
            }

            var dataTable = new MemoryStream();
            var dataInfos = new (int size, int offset)[11];
            for (int i = 0; i < 11; i++)
            {
                int size = entries[i].data.Length;
                int offset = (int)dataTable.Position;
                dataTable.Write(entries[i].data, 0, size);
                int pad = (int)entries[i].maxLen - size;
                if (pad > 0) dataTable.Write(new byte[pad], 0, pad);
                dataInfos[i] = (size, offset);
            }

            w.Write((ReadOnlySpan<byte>)[0x00, 0x50, 0x53, 0x46]);
            w.Write((ReadOnlySpan<byte>)[0x01, 0x01, 0x00, 0x00]);
            w.WriteUInt32LE(0);
            w.WriteUInt32LE(0);
            w.WriteUInt32LE(11);

            for (int i = 0; i < 11; i++)
            {
                w.WriteUInt16LE((ushort)keyOffsets[i]);
                if (entries[i].fmt == 4 && entries[i].maxLen == 4 && (entries[i].key == "ATTRIBUTE" || entries[i].key == "PARENTAL_LEVEL")) w.Write((ReadOnlySpan<byte>)[0x04, 0x04]);
                else if (entries[i].key == "ACCOUNT_ID" || entries[i].key == "PARAMS" || entries[i].key == "PARAMS2") w.Write((ReadOnlySpan<byte>)[0x04, 0x00]);
                else w.Write((ReadOnlySpan<byte>)[0x04, 0x02]);

                w.WriteUInt32LE((uint)dataInfos[i].size);
                w.WriteUInt32LE(entries[i].maxLen);
                w.WriteUInt32LE((uint)dataInfos[i].offset);
            }

            uint keyTableOffset = (uint)ms.Position;
            keyTable.WriteTo(ms);
            uint pad2 = (uint)(ms.Position % 4);
            if (pad2 != 0) w.Write(stackalloc byte[(int)(4 - pad2)]);

            uint dataTableOffset = (uint)ms.Position;
            dataTable.WriteTo(ms);

            ms.Position = 8;
            w.WriteUInt32LE(keyTableOffset);
            w.WriteUInt32LE(dataTableOffset);

            return ms.ToArray();
        }

        public static byte[] MakePfd(ulong version, byte[] sfo, string bkpDir)
        {
            byte[] pfKeyOrig = new byte[20];
            byte[] pfHeaderIv = new byte[16];
            byte[] pfKey = new byte[20];

            if (version == 4) HMACSHA1.HashData(KEYGEN_KEY, pfKeyOrig, pfKey);

            ulong pfIndexSize = 1;
            ulong pfEntrySize = 1;

            byte[] sfoFilename = new byte[65];
            Encoding.ASCII.GetBytes("PARAM.SFO").CopyTo(sfoFilename, 0);

            using var pfEntries = new MemoryStream();
            using var wE = new BinaryWriter(pfEntries);
            wE.WriteUInt64BE(pfIndexSize);
            wE.Write(sfoFilename);
            wE.Write(stackalloc byte[7]);
            wE.Write(stackalloc byte[64]);
            
            Span<byte> sfoMac = stackalloc byte[20];
            HMACSHA1.HashData(SAVEGAME_PARAM_SFO_KEY, sfo, sfoMac);
            wE.Write(sfoMac);
            
            wE.Write(stackalloc byte[20]);
            wE.Write(stackalloc byte[20]);
            wE.Write(stackalloc byte[20]);
            wE.Write(stackalloc byte[40]);
            wE.WriteUInt64BE((ulong)sfo.Length);

            using var pfIndex = new MemoryStream();
            using var wI = new BinaryWriter(pfIndex);
            wI.WriteUInt64BE(pfIndexSize);
            wI.WriteUInt64BE(pfEntrySize);
            wI.WriteUInt64BE(pfEntrySize);
            wI.WriteUInt64BE(0);

            byte[] pfEntrySigTable = new byte[20];
            var ms = new MemoryStream();
            ms.Write(sfoFilename, 0, sfoFilename.Length);
            ms.Write(pfEntries.ToArray(), 80, (int)pfEntries.Length - 80);
            HMACSHA1.HashData(pfKey, ms.ToArray(), pfEntrySigTable);

            Span<byte> pfIndexSig = stackalloc byte[20];
            HMACSHA1.HashData(pfKey, pfIndex.ToArray(), pfIndexSig);
            
            Span<byte> pfEntrySigTableSig = stackalloc byte[20];
            HMACSHA1.HashData(pfKey, pfEntrySigTable, pfEntrySigTableSig);

            byte[] pfHeader = new byte[64];
            using (var msH = new MemoryStream(pfHeader))
            {
                msH.Write(pfEntrySigTableSig);
                msH.Write(pfIndexSig);
                msH.Write(pfKeyOrig);
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
            wP.Write((ReadOnlySpan<byte>)[0, 0, 0, 0]);
            wP.Write("PFDB"u8);
            wP.WriteUInt64BE(version);
            wP.Write(pfHeaderIv);
            wP.Write(pfHeader);
            wP.Write(pfIndex.ToArray());
            wP.Write(pfEntries.ToArray());
            wP.Write(pfEntrySigTable);

            return pfd.ToArray();
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
    }
}