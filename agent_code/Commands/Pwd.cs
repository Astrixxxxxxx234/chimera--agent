using System;
using System.IO;

namespace Chimera.Commands
{
    public static class Pwd
    {
        public static string Execute()
        {
            try
            {
                return Directory.GetCurrentDirectory();
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }
    }
}
