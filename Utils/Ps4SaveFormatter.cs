using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LbpArchiveToolkit.Utils
{
    // Builds a PS4-style sce_sys/param.sfo for shadPS4. Unlike PS3, there is no PARAM.PFD:
    // shadPS4 virtualizes save containers as plain host folders and doesn't verify Sony's
    // save-data signing, so no keystone/PFD equivalent is required for it to accept the save.
    public static class Ps4SaveFormatter
    {
        public static byte[] MakeSfo(string displayName, string bkpName, string npHandle, string description, string titleId, ulong saveDataBlocks)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);

            string mainTitle = "LittleBigPlanet\u21223 Level Backup";
            string subTitle = displayName;

            byte[] savedataBlocks = new byte[8];
            BitConverter.GetBytes((uint)saveDataBlocks).CopyTo(savedataBlocks, 0);

            // Key order + param_fmt values follow a real captured PS4 save param.sfo.
            // Order is NOT alphabetical (unlike PS3) - it must match this exact sequence,
            // and MAINTITLE/SUBTITLE (no underscore) replace PS3's TITLE/SUB_TITLE.
            var entries = new List<(string key, byte fmtHi, byte fmtLo, uint maxLen, byte[] data)>
            {
                ("ACCOUNT_ID",          0x04, 0x00, 8,    new byte[8]),
                ("MAINTITLE",           0x04, 0x02, 128,  TruncateStr(mainTitle, 128)),
                ("SUBTITLE",            0x04, 0x02, 128,  TruncateStr(subTitle, 128)),
                ("DETAIL",              0x04, 0x02, 1024, TruncateStr(description, 1024)),
                ("SAVEDATA_DIRECTORY",  0x04, 0x02, 32,   TruncateStr(bkpName, 32)),
                ("SAVEDATA_LIST_PARAM", 0x04, 0x04, 4,    BitConverter.GetBytes((uint)0)),
                ("TITLE_ID",            0x04, 0x02, 12,   TruncateStr(titleId, 12)),
                ("SAVEDATA_BLOCKS",     0x04, 0x00, 8,    savedataBlocks),
            };

            int count = entries.Count;

            var keyTable = new MemoryStream();
            int[] keyOffsets = new int[count];
            for (int i = 0; i < count; i++)
            {
                keyOffsets[i] = (int)keyTable.Position;
                byte[] kb = Encoding.ASCII.GetBytes(entries[i].key);
                keyTable.Write(kb, 0, kb.Length);
                keyTable.WriteByte(0);
            }

            var dataTable = new MemoryStream();
            var dataInfos = new (int size, int offset)[count];
            for (int i = 0; i < count; i++)
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
            w.WriteUInt32LE((uint)count);

            for (int i = 0; i < count; i++)
            {
                w.WriteUInt16LE((ushort)keyOffsets[i]);
                w.Write((ReadOnlySpan<byte>)[entries[i].fmtHi, entries[i].fmtLo]);
                w.WriteUInt32LE((uint)dataInfos[i].size);
                w.WriteUInt32LE(entries[i].maxLen);
                w.WriteUInt32LE((uint)dataInfos[i].offset);
            }

            uint keyTableOffset = (uint)ms.Position;
            keyTable.WriteTo(ms);

            // Real PS4 param.sfo files don't 4-byte-align the data table — it starts
            // immediately after the key table's last null terminator, no padding.
            uint dataTableOffset = (uint)ms.Position;
            dataTable.WriteTo(ms);

            ms.Position = 8;
            w.WriteUInt32LE(keyTableOffset);
            w.WriteUInt32LE(dataTableOffset);

            return ms.ToArray();
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