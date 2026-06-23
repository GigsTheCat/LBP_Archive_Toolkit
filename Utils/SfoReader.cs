using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LbpArchiveToolkit.Models;

namespace LbpArchiveToolkit.Utils
{
    public static class SfoReader
    {
        public static SfoData GetLevelData(string sfoFilePath)
        {
            var result = new SfoData();
            if (!File.Exists(sfoFilePath)) return result;

            try
            {
                byte[] fileBytes = File.ReadAllBytes(sfoFilePath);
                using (MemoryStream fs = new MemoryStream(fileBytes))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    // Check Magic Header: "\0PSF"
                    byte[] magic = br.ReadBytes(4);
                    if (!magic.SequenceEqual(new byte[] { 0x00, 0x50, 0x53, 0x46 })) 
                        return result;

                    br.ReadUInt32(); // Version
                    uint keyTableStart = br.ReadUInt32();
                    uint dataTableStart = br.ReadUInt32();
                    uint entriesCount = br.ReadUInt32();

                    // Protection: Prevent out-of-bounds reads and high memory allocation 
                    // on corrupted headers. A single directory entry is exactly 16 bytes.
                    long requiredBytes = (long)entriesCount * 16;
                    if (fs.Position + requiredBytes > fs.Length)
                    {
                        return result;
                    }

                    var entries = new List<(ushort keyOffset, uint dataOffset, uint dataLen)>();
                    for (int i = 0; i < entriesCount; i++)
                    {
                        ushort keyOffset = br.ReadUInt16();
                        br.ReadUInt16(); // data format
                        uint dataLen = br.ReadUInt32();
                        br.ReadUInt32(); // max data len
                        uint dataOffset = br.ReadUInt32();
                        entries.Add((keyOffset, dataOffset, dataLen));
                    }

                    // Loop through all entries and look for both SUB_TITLE and DETAIL
                    foreach (var entry in entries)
                    {
                        long keyPos = (long)keyTableStart + entry.keyOffset;
                        if (keyPos < 0 || keyPos >= fs.Length) continue;

                        fs.Position = keyPos;
                        List<byte> keyBytes = new List<byte>();
                        byte b;
                        while (fs.Position < fs.Length && (b = br.ReadByte()) != 0) keyBytes.Add(b);
                        string key = Encoding.UTF8.GetString(keyBytes.ToArray());

                        if (key == "SUB_TITLE" || key == "DETAIL")
                        {
                            if (entry.dataLen > 1024 * 1024) continue; 

                            long dataPos = (long)dataTableStart + entry.dataOffset;
                            if (dataPos < 0 || dataPos >= fs.Length) continue;

                            fs.Position = dataPos;
                            
                            // Bounds safety check to prevent EndOfStreamException on corrupted files
                            int safeLength = (int)Math.Min(entry.dataLen, fs.Length - fs.Position);
                            byte[] dataBytes = br.ReadBytes(safeLength);
                            string decodedText = Encoding.UTF8.GetString(dataBytes).TrimEnd('\0');

                            if (key == "SUB_TITLE") result.Title = decodedText;
                            else if (key == "DETAIL") result.Description = decodedText;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log("SfoReader.GetLevelData", ex);
            }

            return result;
        }
    }
}