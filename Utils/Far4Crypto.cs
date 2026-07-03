using System;
using System.Buffers.Binary;
using System.Runtime.Intrinsics;

namespace LbpArchiveToolkit.Utils
{
    public static class Far4Crypto
    {
        private static readonly uint[] TEA_KEY = new uint[] { 0x1B70CBD, 0x149607D6, 0x7F94DD5, 0x10DB8CA0 };

        public static void XxteaEncrypt(byte[] data, int end)
        {
            if (end <= 0) return;
            int n = (end / 4) - 1;
            if (n < 0) return;

            var v = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(data.AsSpan()[..((n + 1) * 4)]);

            if (BitConverter.IsLittleEndian) SwapEndianness(v);

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

            if (BitConverter.IsLittleEndian) SwapEndianness(v);
        }

        public static void XxteaDecrypt(byte[] data, int end)
        {
            if (end <= 0) return;
            int n = (end / 4) - 1;
            if (n < 0) return;

            var v = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(data.AsSpan()[..((n + 1) * 4)]);

            if (BitConverter.IsLittleEndian) SwapEndianness(v);

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

            if (BitConverter.IsLittleEndian) SwapEndianness(v);
        }

        private static void SwapEndianness(Span<uint> v)
        {
            int i = 0;
            if (Vector512.IsHardwareAccelerated && v.Length >= 16)
            {
                Vector512<byte> shuffleMask = Vector512.Create(
                    (byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12,
                    19, 18, 17, 16, 23, 22, 21, 20, 27, 26, 25, 24, 31, 30, 29, 28,
                    35, 34, 33, 32, 39, 38, 37, 36, 43, 42, 41, 40, 47, 46, 45, 44,
                    51, 50, 49, 48, 55, 54, 53, 52, 59, 58, 57, 56, 63, 62, 61, 60);

                for (; i <= v.Length - 16; i += 16)
                {
                    ref byte byteRef = ref System.Runtime.CompilerServices.Unsafe.As<uint, byte>(ref v[i]);
                    var vec = Vector512.LoadUnsafe(ref byteRef);
                    Vector512.Shuffle(vec, shuffleMask).StoreUnsafe(ref byteRef);
                }
            }
            else if (Vector256.IsHardwareAccelerated && v.Length >= 8)
            {
                Vector256<byte> shuffleMask = Vector256.Create(
                    (byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12,
                    19, 18, 17, 16, 23, 22, 21, 20, 27, 26, 25, 24, 31, 30, 29, 28);

                for (; i <= v.Length - 8; i += 8)
                {
                    ref byte byteRef = ref System.Runtime.CompilerServices.Unsafe.As<uint, byte>(ref v[i]);
                    var vec = Vector256.LoadUnsafe(ref byteRef);
                    Vector256.Shuffle(vec, shuffleMask).StoreUnsafe(ref byteRef);
                }
            }
            else if (Vector128.IsHardwareAccelerated && v.Length >= 4)
            {
                Vector128<byte> shuffleMask = Vector128.Create((byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);

                for (; i <= v.Length - 4; i += 4)
                {
                    ref byte byteRef = ref System.Runtime.CompilerServices.Unsafe.As<uint, byte>(ref v[i]);
                    var vec = Vector128.LoadUnsafe(ref byteRef);
                    Vector128.Shuffle(vec, shuffleMask).StoreUnsafe(ref byteRef);
                }
            }

            for (; i < v.Length; i++)
            {
                v[i] = BinaryPrimitives.ReverseEndianness(v[i]);
            }
        }
    }
}