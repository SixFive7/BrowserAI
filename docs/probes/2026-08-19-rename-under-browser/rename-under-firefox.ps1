# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch measurement: what does a live Firefox do when its own tree is renamed
# underneath it? This is the Chromium measurement in
# .work/rename-under-chromium.ps1 repeated for the other provisioned family,
# because the DECISIONS.md browser-reinstall row is about "a browser" and only
# one of the two was ever measured.
#
# Deliberately the same shape as the Chromium script: same two renames (the
# directory holding the executable, and the revision directory above it), the
# same cwd-elsewhere precaution, and the same browser-killed-first control,
# which is the load-bearing half.
$ErrorActionPreference = 'Stop'

$root    = Join-Path $env:LOCALAPPDATA 'BrowserAI\browsers'
$rev     = Join-Path $root 'firefox-1539'
$inner   = Join-Path $rev 'firefox'
$aside   = Join-Path $root 'firefox-1539.rename-probe'
$asideIn = Join-Path $rev 'firefox.rename-probe'
$exe     = Join-Path $inner 'firefox.exe'
$profile = Join-Path 'C:\Source\SixFive7\BrowserAI\.work' 'rename-probe-profile-ff'
$port    = 9413

function Say($m) { Write-Host ("[{0:HH:mm:ss.fff}] {1}" -f (Get-Date), $m) }

function TryMove($from, $to, $label) {
    try {
        [System.IO.Directory]::Move($from, $to)
        Say "$label : MOVED"
        return $true
    } catch {
        $inner = $_.Exception.InnerException
        $type = if ($inner) { $inner.GetType().Name } else { $_.Exception.GetType().Name }
        Say "$label : REFUSED -- ${type}: $($_.Exception.Message -replace "`n",' ')"
        return $false
    }
}

function Devtools($path, $method = 'GET') {
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$port$path" -Method $method -TimeoutSec 10 -UseBasicParsing
        return @{ ok = $true; code = $r.StatusCode; body = $r.Content }
    } catch {
        return @{ ok = $false; code = -1; body = ($_.Exception.Message -replace "`n", ' ') }
    }
}

function TreeOf($rootPid) {
    $tree = @($rootPid)
    $frontier = @($rootPid)
    while ($frontier.Count -gt 0) {
        $next = @()
        foreach ($p in $frontier) {
            foreach ($c in Get-CimInstance Win32_Process -Filter "ParentProcessId = $p") {
                $tree += $c.ProcessId; $next += $c.ProcessId
            }
        }
        $frontier = $next
    }
    return $tree
}

if (Test-Path $profile) { Remove-Item -Recurse -Force $profile }
New-Item -ItemType Directory -Force $profile | Out-Null

Say "firefox.exe: $exe (exists: $(Test-Path $exe))"

# cwd deliberately in the repository root, so the separate "a directory cannot
# be renamed while a process has it as its current directory" rule cannot be
# the cause of any refusal below.
$proc = Start-Process -FilePath $exe -PassThru -WorkingDirectory 'C:\Source\SixFive7\BrowserAI' -ArgumentList @(
    '-headless'
    '-no-remote'
    '-profile'
    $profile
    '--remote-debugging-port'
    "$port"
    'about:blank'
)

Say "started pid $($proc.Id)"

# Liveness, two independent signals so this is not weaker than the Chromium
# arm: Firefox's Remote Agent answers over HTTP, AND the parent has spawned its
# content/GPU children.
$up = $false
for ($i = 0; $i -lt 150; $i++) {
    Start-Sleep -Milliseconds 200
    if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { Say 'FIREFOX EXITED EARLY'; break }
    $v = Devtools '/json/version'
    if ($v.ok) { $up = $true; Say "devtools up: $($v.body -replace "`n",' ')"; break }
}
$tree = TreeOf $proc.Id
if (-not $up) {
    Say "REMOTE AGENT NEVER CAME UP (tree=$($tree.Count)) -- falling back to the process-tree signal only"
    if ($tree.Count -lt 3) {
        Say "AND FIREFOX NEVER REACHED A MULTI-PROCESS STATE"
        $still = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        if ($still) { $still | Stop-Process -Force }
        exit 1
    }
}

$movedRev = $false
$movedInner = $false

try {
    Say "BEFORE: alive=$($null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) tree=$($tree.Count) processes devtools=$up"
    foreach ($p in $tree) {
        $h = Get-Process -Id $p -ErrorAction SilentlyContinue
        if ($h) { Say ("  tree pid {0} {1}" -f $p, $h.Path) }
    }

    Say '--- with the browser LIVE ---'
    $movedInner = TryMove $inner $asideIn 'live: rename firefox (the executable''s own directory)'
    if ($movedInner) { [System.IO.Directory]::Move($asideIn, $inner); $movedInner = $false; Say 'live: renamed firefox back' }

    $movedRev = TryMove $rev $aside 'live: rename firefox-1539 (the revision directory)'
    if ($movedRev) {
        Start-Sleep -Milliseconds 500
        Say "AFTER: alive=$($null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) tree=$((TreeOf $proc.Id).Count)"
        [System.IO.Directory]::Move($aside, $rev); $movedRev = $false; Say 'renamed back'
    }
}
finally {
    if ($movedInner -and (Test-Path $asideIn)) { [System.IO.Directory]::Move($asideIn, $inner) }
    if ($movedRev -and (Test-Path $aside)) { [System.IO.Directory]::Move($aside, $rev) }

    $still = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    if ($still) { $still | Stop-Process -Force }
    Start-Sleep -Milliseconds 2500

    Say '--- CONTROL: the same two renames with the browser GONE ---'
    if (TryMove $inner $asideIn 'dead: rename firefox') { [System.IO.Directory]::Move($asideIn, $inner) }
    if (TryMove $rev $aside 'dead: rename firefox-1539') { [System.IO.Directory]::Move($aside, $rev) }

    Say "restored: firefox.exe present = $(Test-Path $exe)"
    if (Test-Path $profile) { Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue }
    Say 'done'
}
