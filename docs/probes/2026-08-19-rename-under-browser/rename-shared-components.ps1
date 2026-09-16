# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch measurement: the "full coverage" half of the browser-tree rename
# question. rename-under-chromium.ps1 and rename-under-firefox.ps1 each measure
# ONE family's own two directories. This one measures everything else that lives
# in the provisioned browsers root while a browser of the given family is live:
# the shared components (ffmpeg, winldd) and the browsers root itself, which is
# the directory a wholesale swap would actually touch.
$ErrorActionPreference = 'Stop'

function Say($m) { Write-Host ("[{0:HH:mm:ss.fff}] {1}" -f (Get-Date), $m) }

function TryMove($from, $to, $label) {
    try {
        [System.IO.Directory]::Move($from, $to)
        Say "$label : MOVED"
        return $true
    } catch {
        $ie = $_.Exception.InnerException
        $type = if ($ie) { $ie.GetType().Name } else { $_.Exception.GetType().Name }
        Say "$label : REFUSED -- ${type}: $($_.Exception.Message -replace "`n",' ')"
        return $false
    }
}

function TreeOf($rootPid) {
    $tree = @($rootPid); $frontier = @($rootPid)
    while ($frontier.Count -gt 0) {
        $next = @()
        foreach ($p in $frontier) {
            foreach ($c in Get-CimInstance Win32_Process -Filter "ParentProcessId = $p") { $tree += $c.ProcessId; $next += $c.ProcessId }
        }
        $frontier = $next
    }
    return $tree
}

$browsers = Join-Path $env:LOCALAPPDATA 'BrowserAI\browsers'
$profile  = Join-Path 'C:\Source\SixFive7\BrowserAI\.work' 'rename-probe-profile-shared'

foreach ($family in @('chromium', 'firefox')) {
    Say "================ family: $family ================"

    if ($family -eq 'chromium') {
        $exe  = Join-Path $browsers 'chromium-1237\chrome-win64\chrome.exe'
        $args = @('--headless=new', "--user-data-dir=$profile", '--no-first-run', '--no-default-browser-check', '--disable-gpu', 'about:blank')
    } else {
        $exe  = Join-Path $browsers 'firefox-1539\firefox\firefox.exe'
        $args = @('-headless', '-no-remote', '-profile', $profile, 'about:blank')
    }

    if (Test-Path $profile) { Remove-Item -Recurse -Force $profile }
    New-Item -ItemType Directory -Force $profile | Out-Null

    $proc = Start-Process -FilePath $exe -PassThru -WorkingDirectory 'C:\Source\SixFive7\BrowserAI' -ArgumentList $args
    Say "started pid $($proc.Id)"

    $tree = @()
    for ($i = 0; $i -lt 150; $i++) {
        Start-Sleep -Milliseconds 200
        if (-not (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) { break }
        $tree = TreeOf $proc.Id
        if ($tree.Count -ge 4) { break }
    }
    Say "live: tree=$($tree.Count) processes"

    $moved = @{}
    try {
        foreach ($component in @('ffmpeg-1011', 'winldd-1007')) {
            $from = Join-Path $browsers $component
            $to   = "$from.rename-probe"
            if (-not (Test-Path $from)) { Say "live: $component ABSENT -- not measured"; continue }
            if (TryMove $from $to "live ($family): rename $component (shared component)") {
                [System.IO.Directory]::Move($to, $from); Say "  renamed $component back"
            }
        }

        # The browsers root itself: the directory a wholesale swap would touch.
        $rootTo = "$browsers.rename-probe"
        if (TryMove $browsers $rootTo "live ($family): rename the browsers ROOT") {
            $moved['root'] = $rootTo
            [System.IO.Directory]::Move($rootTo, $browsers); $moved.Remove('root'); Say '  renamed the browsers root back'
        }
    }
    finally {
        if ($moved.ContainsKey('root') -and (Test-Path $moved['root'])) { [System.IO.Directory]::Move($moved['root'], $browsers) }
        $still = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        if ($still) { $still | Stop-Process -Force }
        Start-Sleep -Milliseconds 2500
    }

    Say "--- CONTROL ($family gone) ---"
    foreach ($component in @('ffmpeg-1011', 'winldd-1007')) {
        $from = Join-Path $browsers $component
        $to   = "$from.rename-probe"
        if (-not (Test-Path $from)) { continue }
        if (TryMove $from $to "dead: rename $component") { [System.IO.Directory]::Move($to, $from) }
    }
    $rootTo = "$browsers.rename-probe"
    if (TryMove $browsers $rootTo 'dead: rename the browsers ROOT') { [System.IO.Directory]::Move($rootTo, $browsers) }

    if (Test-Path $profile) { Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue }
}

Say "restored: chromium present = $(Test-Path (Join-Path $browsers 'chromium-1237\chrome-win64\chrome.exe'))"
Say "restored: firefox  present = $(Test-Path (Join-Path $browsers 'firefox-1539\firefox\firefox.exe'))"
Say "restored: ffmpeg   present = $(Test-Path (Join-Path $browsers 'ffmpeg-1011'))"
Say "restored: winldd   present = $(Test-Path (Join-Path $browsers 'winldd-1007'))"
Say 'done'
