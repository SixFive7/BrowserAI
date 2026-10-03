# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Row 115's re-take, 2026-10-03. Puts a headed browser window on the screen, four
# times, about five seconds each, with the maintainer's approval for this row.
# Order: Firefox with the flag, Firefox without, Chromium with, Chromium without --
# the one arm expected to take the foreground goes last, so it cannot spoil the
# condition of the others. Every process this starts is ended by pid at the end of
# its arm, and only after its path is checked against the browsers root.
param([int]$PollMs = 5000, [string[]]$Only = @(), [int]$ConditionWaitMs = 30000)
$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\stale-scratch'
$out = Join-Path $S 'out\row115'
New-Item -ItemType Directory -Force $out | Out-Null
Add-Type -Path (Join-Path $S 'rigs\row115\Fg.cs')
$browsers = Join-Path $env:LOCALAPPDATA 'BrowserAI\browsers'
$log = Join-Path $out ('foreground-' + (Get-Date).ToUniversalTime().ToString('HHmmss') + '.log')
function Say($m) { $l = ('[{0:yyyy-MM-ddTHH:mm:ss.fffZ}] {1}' -f (Get-Date).ToUniversalTime(), $m); Add-Content -Path $log -Value $l; Write-Host $l }

$ancestors = @(); $p = $PID
for ($i = 0; $i -lt 20 -and $p; $i++) { $c = Get-CimInstance Win32_Process -Filter "ProcessId=$p"; if (-not $c) { break }; $ancestors += [int]$c.ProcessId; $p = $c.ParentProcessId }
Say "SPI_GETFOREGROUNDLOCKTIMEOUT = $([Fg]::ForegroundLockTimeout()) ms"
Say "ancestors of this process: $($ancestors -join ',')"

$arms = @(
  @{ name = 'firefox-flag';    exe = Join-Path $browsers 'firefox-1553\firefox\firefox.exe';     flag = $true;  args = { param($d) "-no-remote -profile `"$d`" about:blank" } },
  @{ name = 'firefox-noflag';  exe = Join-Path $browsers 'firefox-1553\firefox\firefox.exe';     flag = $false; args = { param($d) "-no-remote -profile `"$d`" about:blank" } },
  @{ name = 'chromium-flag';   exe = Join-Path $browsers 'chromium-1247\chrome-win64\chrome.exe'; flag = $true;  args = { param($d) "--user-data-dir=`"$d`" --no-first-run --no-default-browser-check about:blank" } },
  @{ name = 'chromium-noflag'; exe = Join-Path $browsers 'chromium-1247\chrome-win64\chrome.exe'; flag = $false; args = { param($d) "--user-data-dir=`"$d`" --no-first-run --no-default-browser-check about:blank" } }
)

foreach ($arm in $arms) {
  if ($Only.Count -gt 0 -and $Only -notcontains $arm.name) { continue }
  # The row's condition: a window owned by an ANCESTOR of this process in the foreground at launch.
  $waited = [Diagnostics.Stopwatch]::StartNew()
  while (($ancestors -notcontains [Fg]::ForegroundPid()) -and $waited.ElapsedMilliseconds -lt $ConditionWaitMs) { Start-Sleep -Milliseconds 250 }
  if ($ancestors -notcontains [Fg]::ForegroundPid()) { Say "--- ARM $($arm.name): SKIPPED, no ancestor window in the foreground within $ConditionWaitMs ms (foreground pid=$([Fg]::ForegroundPid()))"; continue }
  $profile = Join-Path $out ("profile-" + $arm.name)
  New-Item -ItemType Directory -Force $profile | Out-Null
  $fgPid = [Fg]::ForegroundPid()
  Say "--- ARM $($arm.name): foreground before launch pid=$fgPid ancestor=$($ancestors -contains $fgPid)"
  $rootPid = 0
  $lines = [Fg]::Arm($arm.exe, (& $arm.args $profile), $out, $arm.flag, $PollMs, [ref]$rootPid)
  foreach ($l in $lines) { Say "  $l" }
  # End exactly the tree this arm launched: every pid recorded, each checked against the browsers root first.
  $treeLine = $lines | Where-Object { $_ -like 'TREE *' } | Select-Object -Last 1
  $pids = @(); if ($treeLine) { $pids = $treeLine.Substring(5).Split(',') | ForEach-Object { [int]$_ } }
  foreach ($q in $pids) {
    $proc = Get-Process -Id $q -ErrorAction SilentlyContinue
    if ($proc -and $proc.Path -and $proc.Path.StartsWith($browsers, [StringComparison]::OrdinalIgnoreCase)) { Stop-Process -Id $q -Force -ErrorAction SilentlyContinue }
  }
  Start-Sleep -Milliseconds 1500
  $left = @($pids | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
  Say "  ended at $(([DateTime]::UtcNow).ToString('HH:mm:ss.fff'))Z; still alive from the tree: $($left.Count); foreground after: pid=$([Fg]::ForegroundPid())"
}
Say 'done'
