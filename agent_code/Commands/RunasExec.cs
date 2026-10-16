using System;
using System.Diagnostics;

namespace Chimera.Commands
{
    // RunasExec — runs a command using the "runas" shell verb via Process.Start.
    // This triggers a UAC elevation prompt on the target desktop.
    //
    // Study notes:
    //   - UseShellExecute = true is required for Verb = "runas" to work
    //   - The UAC prompt appears on the *secure desktop* (dimmed screen)
    //   - If the process is already elevated (high integrity), no prompt appears
    //   - Observable in: Sysmon EID 1 (ProcessCreate) with ParentImage = consent.exe
    //     after the user accepts; EID 1 with IntegrityLevel = High on the child
    //   - If UAC is set to "never notify", runas still spawns elevated with no prompt
    //
    // Usage: runas_exec <command>
    // Example: runas_exec whoami /groups
    public static class RunasExec
    {
        public static string Execute(string command)
        {
            if (string.IsNullOrEmpty(command))
                return "[error] No command specified.";

            try
            {
                // Split command into filename + args
                string fileName;
                string arguments = "";

                int spaceIdx = command.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    fileName  = command.Substring(0, spaceIdx);
                    arguments = command.Substring(spaceIdx + 1);
                }
                else
                {
                    fileName = command;
                }

                var psi = new ProcessStartInfo
                {
                    FileName        = fileName,
                    Arguments       = arguments,
                    Verb            = "runas",       // triggers UAC
                    UseShellExecute = true,          // required for Verb to work
                    // Note: cannot redirect stdout/stderr when UseShellExecute = true
                    // Output won't be captured — this is a UAC study tool, not a shell
                };

                Process proc = Process.Start(psi);

                if (proc != null)
                {
                    return $"[+] Process launched with runas verb.\n" +
                           $"    PID:     {proc.Id}\n" +
                           $"    File:    {fileName}\n" +
                           $"    Args:    {arguments}\n\n" +
                           $"Note: stdout/stderr cannot be captured when using UseShellExecute=true.\n" +
                           $"Watch the target desktop for the UAC prompt.\n\n" +
                           $"IR artefacts:\n" +
                           $"  Sysmon EID 1: ProcessCreate with ParentImage=consent.exe (if accepted)\n" +
                           $"  Sysmon EID 1: IntegrityLevel=High on child process\n" +
                           $"  Security Event 4688: A new process has been created (if auditing enabled)";
                }
                else
                {
                    return "[!] Process.Start returned null — UAC prompt may have been dismissed.";
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED — user clicked No on UAC prompt
                return "[!] UAC prompt was cancelled by user (ERROR_CANCELLED 1223).\n" +
                       "    This is expected — user denied elevation.";
            }
            catch (Exception ex)
            {
                return $"[error] {ex.GetType().Name}: {ex.Message}";
            }
        }
    }
}
