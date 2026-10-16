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
                    return $"[error] Directory not found: {path}";

                var sb = new StringBuilder();
                sb.AppendLine($"Directory: {path}");
                sb.AppendLine($"{"Name",-40} {"Type",-8} {"Size",12} {"Modified"}");
                sb.AppendLine(new string('-', 80));

                // Directories first
                foreach (var dir in Directory.GetDirectories(path))
                {
                    var info = new DirectoryInfo(dir);
                    sb.AppendLine($"{info.Name,-40} {"<DIR>",-8} {"",12} {info.LastWriteTime:yyyy-MM-dd HH:mm}");
                }

                // Then files
                foreach (var file in Directory.GetFiles(path))
                {
                    var info = new FileInfo(file);
                    sb.AppendLine($"{info.Name,-40} {"FILE",-8} {info.Length,12} {info.LastWriteTime:yyyy-MM-dd HH:mm}");
                }

                return sb.ToString();
            }
            catch (UnauthorizedAccessException)
            {
                return $"[error] Access denied: {path}";
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }
    }
}
