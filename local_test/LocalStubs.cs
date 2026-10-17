// Stubs for local test build - no Mythic server needed
using System;
namespace Chimera {
    public static class C2Http {
        public static string Send(string json) { return "{}"; }
        public static void PostResponse(string uuid, string taskID, string output) { }
        public static string CheckIn() { return null; }
        public static string GetTasking(string uuid) { return null; }
        public static void Sleep() { System.Threading.Thread.Sleep(500); }
    }
    public static class Tasking {
        public static string CallbackUUID = "local-test-uuid";
        public static string ExtractJsonValue(string json, string key) { return null; }
    }
}
