# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig: a session directory's two files across a hard kill, without BrowserAI.
# One child holds browserai.lock the way LockFile.Hold does (ReadWrite, FileShare.Read);
# another holds browserai.data the way SessionStore does (SQLite, WAL) with one row still
# in flight. Both are in one kill-on-close job, which is then closed.
$ErrorActionPreference = 'Stop'
$root = 'C:\Source\SixFive7\BrowserAI\.work\hard-kill'
$rig = Join-Path $root 'rig'
$node = 'C:\Source\SixFive7\BrowserAI\payload\node\node.exe'
Add-Type -Path (Join-Path $rig 'hk.cs')
$out = [ordered]@{ startedUtc = (Get-Date).ToUniversalTime().ToString('o') }
$results = @()
foreach ($rep in 1..3) {
    $dir = Join-Path $root "sim\session-$rep"
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    [void][System.IO.Directory]::CreateDirectory($dir)
    $lock = Join-Path $dir 'browserai.lock'
    $env1 = [System.Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($e in [Environment]::GetEnvironmentVariables().GetEnumerator()) { $env1[[string]$e.Key] = [string]$e.Value }
    $job = [HkWin]::CreateKillOnCloseJob()
    $holderScript = "`$f = [System.IO.FileStream]::new('$lock', 'CreateNew', 'ReadWrite', 'Read', 1); `$b = [System.Text.Encoding]::UTF8.GetBytes('{""pid"":' + `$PID + '}'); `$f.Write(`$b, 0, `$b.Length); `$f.Flush(); [Console]::Out.WriteLine('READY'); [Console]::Out.Flush(); Start-Sleep -Seconds 600"
    $lh = [HkChild]::Start((Get-Command pwsh).Source, [string[]]@('-NoProfile', '-NonInteractive', '-Command', $holderScript), $dir, $env1)
    [HkWin]::Assign($job, $lh.P)
    $dh = [HkChild]::Start($node, [string[]]@((Join-Path $rig 'sim-session-store.js'), 'hold', $dir), $dir, $env1)
    [HkWin]::Assign($job, $dh.P)
    $l1 = $lh.ReadLine(30000); $l2 = $dh.ReadLine(30000)
    $r = [ordered]@{ rep = $rep; lockHolder = $l1; storeHolder = $l2 }
    # while held: what the probe sees
    $r.probeWhileHeld = [HkWin]::TryOpenRw($lock)
    try { $fs = [System.IO.FileStream]::new($lock, 'Open', 'ReadWrite', 'Read', 1); $fs.Dispose(); $r.lockFileProbeWhileHeld = 'opened' } catch { $r.lockFileProbeWhileHeld = $_.Exception.InnerException.Message ?? $_.Exception.Message }
    $handles = [System.Collections.Generic.List[HkWin+ProcHandle]]::new()
    foreach ($p in [HkWin]::JobPids($job)) { $h = [HkWin]::Open($p); if ($h) { $handles.Add($h) } }
    $t0 = [HkWin]::NowMs()
    [void][HkWin]::CloseHandle($job)
    $alive = [HkWin]::WaitAll($handles, 10000)
    $r.killToExitedMs = [HkWin]::NowMs() - $t0
    $r.alive = $alive.Count
    # LockFile.Probe semantics: ReadWrite with FileShare.Read; a sharing violation is Held.
    $opened = $null
    while (([HkWin]::NowMs() - $t0) -lt 5000) {
        try { $fs = [System.IO.FileStream]::new($lock, 'Open', 'ReadWrite', 'Read', 1); $fs.Dispose(); $opened = [HkWin]::NowMs() - $t0; break } catch { Start-Sleep -Milliseconds 2 }
    }
    $r.lockReopenableAfterMs = $opened
    $r.lockContent = [System.IO.File]::ReadAllText($lock)
    $r.filesAfterKill = @(Get-ChildItem -LiteralPath $dir -Force | ForEach-Object { "$($_.Name):$($_.Length)" })
    $rd = & $node (Join-Path $rig 'sim-session-store.js') 'read' $dir 2>$null
    $r.reader = $rd
    [HkWin]::CloseAll($handles)
    $results += $r
}
$out.results = $results
$out | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root 'results\sim-session.json') -Encoding utf8
$out | ConvertTo-Json -Depth 10
