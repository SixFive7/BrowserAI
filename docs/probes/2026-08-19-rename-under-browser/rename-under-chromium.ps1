# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch measurement: what does a live Chromium do when its own tree is renamed
# underneath it? DECISIONS.md's browser-reinstall row is left standing on the
# grounds that "nothing has measured what Chromium then does".
$ErrorActionPreference = 'Stop'

$root    = Join-Path $env:LOCALAPPDATA 'BrowserAI\browsers'
$rev     = Join-Path $root 'chromium-1237'
$inner   = Join-Path $rev 'chrome-win64'
$aside   = Join-Path $root 'chromium-1237.rename-probe'
$asideIn = Join-Path $rev 'chrome-win64.rename-probe'
$exe     = Join-Path $inner 'chrome.exe'
$profile = Join-Path 'C:\Source\SixFive7\BrowserAI\.work' 'rename-probe-profile'
$port    = 9412

function Say($m) { Write-Host ("[{0:HH:mm:ss.fff}] {1}" -f (Get-Date), $m) }

function TryMove($from, $to, $label) {
    try {
        [System.IO.Directory]::Move($from, $to)
        Say "$label : MOVED"
        return $true
    } catch {
        Say "$label : REFUSED -- $($_.Exception.InnerException?.GetType().Name ?? $_.Exception.GetType().Name): $($_.Exception.Message -replace "`n",' ')"
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

if (Test-Path $profile) { Remove-Item -Recurse -Force $profile }
New-Item -ItemType Directory -Force $profile | Out-Null

Say "chrome.exe: $exe (exists: $(Test-Path $exe))"

$proc = Start-Process -FilePath $exe -PassThru -WorkingDirectory 'C:\Source\SixFive7\BrowserAI' -ArgumentList @(
    '--headless=new'
    "--user-data-dir=$profile"
    "--remote-debugging-port=$port"
    '--no-first-run'
    '--no-default-browser-check'
    '--disable-gpu'
    'about:blank'
)

Say "started pid $($proc.Id)"

$up = $false
for ($i = 0; $i -lt 100; $i++) {
    $v = Devtools '/json/version'
    if ($v.ok) { $up = $true; break }
    Start-Sleep -Milliseconds 200
}
if (-not $up) { Say 'DEVTOOLS NEVER CAME UP'; $proc | Stop-Process -Force; exit 1 }

$movedRev = $false
$movedInner = $false

try {
    $tree = @($proc.Id)
    $frontier = @($proc.Id)
    while ($frontier.Count -gt 0) {
        $next = @()
        foreach ($p in $frontier) {
            foreach ($c in Get-CimInstance Win32_Process -Filter "ParentProcessId = $p") {
                $tree += $c.ProcessId; $next += $c.ProcessId
            }
        }
        $frontier = $next
    }

    Say "BEFORE: alive=$($null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) tree=$($tree.Count) processes"
    $t1 = Devtools '/json/new?about:blank' 'PUT'
    Say "BEFORE: new target ok=$($t1.ok) code=$($t1.code)"

    Say '--- with the browser LIVE ---'
    $movedInner = TryMove $inner $asideIn 'live: rename chrome-win64 (the executable''s own directory)'
    if ($movedInner) { [System.IO.Directory]::Move($asideIn, $inner); $movedInner = $false; Say 'live: renamed chrome-win64 back' }

    $movedRev = TryMove $rev $aside 'live: rename chromium-1237 (the revision directory)'

    if ($movedRev) {
        Start-Sleep -Milliseconds 500
        Say "AFTER: alive=$($null -ne (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue))"
        $v2 = Devtools '/json/version'
        Say "AFTER: /json/version ok=$($v2.ok)"
        for ($i = 0; $i -lt 3; $i++) {
            $t = Devtools "/json/new?about:blank#$i" 'PUT'
            Say "AFTER: spawn attempt $i ok=$($t.ok) code=$($t.code) body=$($t.body)"
        }
        [System.IO.Directory]::Move($aside, $rev); $movedRev = $false; Say 'renamed back'
    }
    else {
        # WHY. A directory cannot be renamed while any process has it as its
        # current directory, or while anything holds a handle to it without
        # FILE_SHARE_DELETE. Chromium's own cwd is the first suspect, so it is
        # read and not assumed.
        foreach ($p in $tree) {
            $h = Get-Process -Id $p -ErrorAction SilentlyContinue
            if ($h) { Say ("  tree pid {0} {1}" -f $p, $h.Path) }
        }
    }
}
finally {
    if ($movedInner -and (Test-Path $asideIn)) { [System.IO.Directory]::Move($asideIn, $inner) }
    if ($movedRev -and (Test-Path $aside)) { [System.IO.Directory]::Move($aside, $rev) }

    $still = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    if ($still) { $still | Stop-Process -Force }
    Start-Sleep -Milliseconds 1500

    Say '--- CONTROL: the same two renames with the browser GONE ---'
    if (TryMove $inner $asideIn 'dead: rename chrome-win64') { [System.IO.Directory]::Move($asideIn, $inner) }
    if (TryMove $rev $aside 'dead: rename chromium-1237') { [System.IO.Directory]::Move($aside, $rev) }

    Say "restored: chrome.exe present = $(Test-Path $exe)"
    if (Test-Path $profile) { Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue }
    Say 'done'
}
