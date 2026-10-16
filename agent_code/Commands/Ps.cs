using System;
using System.Diagnostics;
using System.Text;

namespace Chimera.Commands
{
    public static class Ps
    {
        public static string Execute()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(string.Format("{0,-8} {1,-45} {2,-20} {3}",
                    "PID", "Name", "User", "Arch"));
                sb.AppendLine(new string('-', 90));

                foreach (Process p in Process.GetProcesses())
                {
                    string user = "";
                    string arch = "";
                    try
                    {
                        // Try to get owner via WMI query — best-effort
                        var searcher = new System.Management.ManagementObjectSearcher(
                            "SELECT * FROM Win32_Process WHERE ProcessId=" + p.Id);
                        foreach (System.Management.ManagementObject obj in searcher.Get())
                        {
                            string[] args = new string[2];
                            int ret = Convert.ToInt32(obj.InvokeMethod("GetOwner", args));
                            if (ret == 0)
                                user = (args[1] ?? "") + "\\" + (args[0] ?? "");
                        }
                    }
                    catch { user = ""; }

                    try
                    {
                        bool is32 = false;
                        IsWow64Process(p.Handle, out is32);
                        arch = is32 ? "x86" : "x64";
                    }
                    catch { arch = "?"; }

                    sb.AppendLine(string.Format("{0,-8} {1,-45} {2,-20} {3}",
                        p.Id, Truncate(p.ProcessName, 44), Truncate(user, 19), arch));
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "[error] ps: " + ex.Message;
            }
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool IsWow64Process(IntPtr hProcess, out bool wow64);
    }
}
