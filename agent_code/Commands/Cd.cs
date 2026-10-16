using System;
using System.IO;

namespace Chimera.Commands
{
    public static class Cd
    {
        public static string Execute(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                    return "[error] No path specified";

                // Expand environment variables e.g. %USERPROFILE%
                path = Environment.ExpandEnvironmentVariables(path);

                if (!Directory.Exists(path))
                    return $"[error] Directory not found: {path}";

                Directory.SetCurrentDirectory(path);
                return Directory.GetCurrentDirectory();
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }
    }
}
