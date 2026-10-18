using System;
using System.IO;

namespace Chimera
{
    // Simple file logger — writes timestamped lines to C:\Temp\chimera_log.txt
    // Designed for debug builds; can be stripped for stealth builds.
    public static class Logger
    {
        private static readonly string LogPath = @"C:\Temp\chimera_log.txt";
        private static readonly object _lock   = new object();

        static Logger()
        {
            try { Directory.CreateDirectory(@"C:\Temp"); } catch { }
            Write("===== CHIMERA STARTED " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====");
        }

        public static void Write(string msg)
        {
            try
            {
                lock (_lock)
                {
                    string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg;
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void Ok(string msg)   => Write("[OK]  " + msg);
        public static void Err(string msg)  => Write("[ERR] " + msg);
        public static void Info(string msg) => Write("[..] " + msg);
    }
}
