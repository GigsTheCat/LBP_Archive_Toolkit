using System.IO;

namespace LbpArchiveToolkit.Utils
{
    public static class BinaryExtensions
    {
        public static void WriteUInt32BE(this BinaryWriter w, uint val)
        {
            w.Write((byte)(val >> 24)); w.Write((byte)(val >> 16)); w.Write((byte)(val >> 8)); w.Write((byte)val);
        }

        public static void WriteUInt16BE(this BinaryWriter w, ushort val)
        {
            w.Write((byte)(val >> 8)); w.Write((byte)val);
        }

        public static void WriteUInt64BE(this BinaryWriter w, ulong val)
        {
            w.Write((byte)(val >> 56)); w.Write((byte)(val >> 48)); w.Write((byte)(val >> 40)); w.Write((byte)(val >> 32));
            w.Write((byte)(val >> 24)); w.Write((byte)(val >> 16)); w.Write((byte)(val >> 8)); w.Write((byte)val);
        }

        public static void WriteUInt32LE(this BinaryWriter w, uint val)
        {
            w.Write((byte)val); w.Write((byte)(val >> 8)); w.Write((byte)(val >> 16)); w.Write((byte)(val >> 24));
        }

        public static void WriteUInt16LE(this BinaryWriter w, ushort val)
        {
            w.Write((byte)val); w.Write((byte)(val >> 8));
        }
    }
}