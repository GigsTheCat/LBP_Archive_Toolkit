using System;
using System.Buffers.Binary;
using System.IO;

namespace LbpArchiveToolkit.Utils
{
    public static class BinaryExtensions
    {
        public static void WriteUInt32BE(this BinaryWriter w, uint val)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, val);
            w.Write(buffer);
        }

        public static void WriteUInt16BE(this BinaryWriter w, ushort val)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, val);
            w.Write(buffer);
        }

        public static void WriteUInt64BE(this BinaryWriter w, ulong val)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(buffer, val);
            w.Write(buffer);
        }

        public static void WriteUInt32LE(this BinaryWriter w, uint val)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, val);
            w.Write(buffer);
        }

        public static void WriteUInt16LE(this BinaryWriter w, ushort val)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, val);
            w.Write(buffer);
        }
    }
}