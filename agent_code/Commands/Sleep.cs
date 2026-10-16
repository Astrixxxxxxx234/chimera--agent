using System;

namespace Chimera.Commands
{
    public static class Sleep
    {
        public static string Execute(string parameters)
        {
            try
            {
                // Parameters: {"interval": 30, "jitter": 20}
                string intervalStr = Tasking.ExtractJsonValue(parameters, "interval");
                string jitterStr   = Tasking.ExtractJsonValue(parameters, "jitter");

                if (string.IsNullOrEmpty(intervalStr))
                    return "[error] No interval specified";

                if (!int.TryParse(intervalStr, out int interval) || interval < 1)
                    return "[error] Invalid interval — must be positive integer";

                int jitter = Config.Jitter; // keep current jitter if not specified
                if (!string.IsNullOrEmpty(jitterStr))
                    int.TryParse(jitterStr, out jitter);

                // Clamp jitter to 0-100
                jitter = Math.Max(0, Math.Min(100, jitter));

                // Update live config
                Config.SleepInterval = interval;
                Config.Jitter        = jitter;

                return $"[+] Sleep updated: {interval}s interval, {jitter}% jitter";
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }
    }
}
