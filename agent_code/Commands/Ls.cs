using System;
using System.IO;
using System.Text;

namespace Chimera.Commands
{
    public static class Ls
    {
        public static string Execute(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || path == ".")
                    path = Directory.GetCurrentDirectory();

                if (!Directory.Exists(path))
                    return "[error] Directory not found: " + path;

                var sb = new StringBuilder();
                sb.AppendLine("Directory: " + path);
                sb.AppendLine(string.Format("{0,-40} {1,-8} {2,12} {3}", "Name", "Type", "Size", "Modified"));
                sb.AppendLine(new string('-', 80));

                // Directories first
                foreach (var dir in Directory.GetDirectories(path))
                {
                    var info = new DirectoryInfo(dir);
                    sb.AppendLine(string.Format("{0,-40} {1,-8} {2,12} {3:yyyy-MM-dd HH:mm}",
                        info.Name, "<DIR>", "", info.LastWriteTime));
                }

                // Then files
                foreach (var file in Directory.GetFiles(path))
                {
                    var info = new FileInfo(file);
                    sb.AppendLine(string.Format("{0,-40} {1,-8} {2,12} {3:yyyy-MM-dd HH:mm}",
                        info.Name, "FILE", info.Length, info.LastWriteTime));
                }

                return sb.ToString();
            }
            catch (UnauthorizedAccessException)
            {
                return "[error] Access denied: " + path;
            }
            catch (Exception ex)
            {
                return "[error] " + ex.Message;
            }
        }
    }
}
