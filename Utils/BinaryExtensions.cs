using System.Buffers.Binary;
using System.IO;

namespace LbpArchiveToolkit.Utils
{
    public static class BinaryExtensions
    {
        public static void WriteUInt32BE(this BinaryWriter w, uint val)
        {
            w.Write(BinaryPrimitives.ReverseEndianness(val));
        }

        public static void WriteUInt16BE(this BinaryWriter w, ushort val)
        {
            w.Write(BinaryPrimitives.ReverseEndianness(val));
        }

        public static void WriteUInt64BE(this BinaryWriter w, ulong val)
        {
            w.Write(BinaryPrimitives.ReverseEndianness(val));
        }

        public static void WriteUInt32LE(this BinaryWriter w, uint val)
        {
            w.Write(val);
        }

        public static void WriteUInt16LE(this BinaryWriter w, ushort val)
        {
            w.Write(val);
        }
    }
}