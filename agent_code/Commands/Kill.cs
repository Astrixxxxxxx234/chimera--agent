using System;
using System.Diagnostics;

namespace Chimera.Commands
{
    public static class Kill
    {
        public static string Execute()
        {
            try
            {
                // Let Mythic know we're exiting before we die
                // The beacon loop will catch the Environment.Exit
                return "__KILL__";
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }
    }
}
