# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Row 32, arms (b) and (c), 2026-10-03: the provisioned chrome.exe launched directly
# and HEADED with a short command line, (b) against an existing directory whose
# DACL is empty and (c) against a path occupied by a file. (c) is expected to put
# the "Failed to create data directory" dialog on the screen and, once that is
# closed, a browser window on the default profile; both are closed or ended as soon
# as they have been read. Approved for this row by the maintainer on 2026-10-03.
param([string]$Arm = 'both')
$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\stale-scratch'
$out = Join-Path $S 'out\row32'
New-Item -ItemType Directory -Force $out | Out-Null
if (-not ('Dlg' -as [type])) { Add-Type -Path (Join-Path $S 'rigs\row32\Dlg.cs') }
$browsers = Join-Path $env:LOCALAPPDATA 'BrowserAI\browsers'
$exe = Join-Path $browsers 'chromium-1247\chrome-win64\chrome.exe'
$fallback = Join-Path $env:LOCALAPPDATA 'Google\Chrome for Testing'
$log = Join-Path $out 'direct-arms.log'
function Say($m) { $l = ('[{0:yyyy-MM-ddTHH:mm:ss.fffZ}] {1}' -f (Get-Date).ToUniversalTime(), $m); Add-Content -Path $log -Value $l; Write-Host $l }
function Read-Tree($root) {
  $tree = [Dlg]::Tree($root)
  $all = @(Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, ExecutablePath, CommandLine)
  $members = @($all | Where-Object { $tree.Contains([int]$_.ProcessId) -and $_.ExecutablePath -and $_.ExecutablePath.StartsWith($browsers, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object {
    [pscustomobject]@{ pid = [int]$_.ProcessId; parent = [int]$_.ParentProcessId; type = $(if ($_.CommandLine -match '--type=(\S+)') { $Matches[1] } else { 'browser' }) } })
  $bp = @($members | ForEach-Object { $_.pid })
  $roots = @($members | Where-Object { $bp -notcontains $_.parent })
  [pscustomobject]@{
    browserProcesses = $members.Count
    types = (($members | Group-Object type | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ',')
    visibleWindows = @([Dlg]::Visible($tree))
    messageWindows = @([Dlg]::MessageWindows($tree))
    restart = @($roots | ForEach-Object { "pid $($_.pid): $([Dlg]::Restart($_.pid))" })
  } | ConvertTo-Json -Compress -Depth 4
}
function EndTree($pids) {
  foreach ($q in $pids) {
    $p = Get-Process -Id $q -ErrorAction SilentlyContinue
    if ($p -and $p.Path -and $p.Path.StartsWith($browsers, [StringComparison]::OrdinalIgnoreCase)) { Stop-Process -Id $q -Force -ErrorAction SilentlyContinue }
  }
}
function Launch($dataDir) {
  $psi = [System.Diagnostics.ProcessStartInfo]::new($exe)
  $psi.UseShellExecute = $false
  $psi.WorkingDirectory = $out
  foreach ($a in @("--user-data-dir=$dataDir", '--no-first-run', '--no-default-browser-check', 'about:blank')) { $psi.ArgumentList.Add($a) }
  return [System.Diagnostics.Process]::Start($psi)
}

if ($Arm -in 'both', 'b') {
  $dir = Join-Path $out 'b-dacl\profile-with-an-empty-dacl'
  New-Item -ItemType Directory -Force $dir | Out-Null
  & icacls $dir /inheritance:r | Out-Null
  Say "(b) DACL now: $((& icacls $dir) -join ' / ')"
  Say "(b) fallback directory before: exists=$(Test-Path $fallback)"
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = Launch $dir
  Say "(b) launched pid $($p.Id)"
  $seen = @{}
  while (-not $p.HasExited -and $sw.ElapsedMilliseconds -lt 15000) {
    foreach ($w in [Dlg]::Visible([Dlg]::Tree($p.Id))) { if (-not $seen.ContainsKey($w)) { $seen[$w] = $sw.ElapsedMilliseconds; Say "(b) visible window at $($sw.ElapsedMilliseconds) ms: $w" } }
    Start-Sleep -Milliseconds 100
  }
  if ($p.HasExited) { Say "(b) exited after $($sw.ElapsedMilliseconds) ms with code $($p.ExitCode)" } else { Say "(b) STILL RUNNING at 15 s"; Say "(b) reading: $(Read-Tree $p.Id)"; EndTree (@([Dlg]::Tree($p.Id))) }
  Say "(b) visible windows seen: $($seen.Count); fallback directory after: exists=$(Test-Path $fallback)"
  & icacls $dir /reset | Out-Null
  Remove-Item -Recurse -Force (Join-Path $out 'b-dacl')
}

if ($Arm -in 'both', 'c') {
  New-Item -ItemType Directory -Force (Join-Path $out 'c-dialog') | Out-Null
  $file = Join-Path $out 'c-dialog\occupied-by-a-file'
  Set-Content -Path $file -Value 'a file where the profile directory should be'
  Say "(c) fallback directory before: exists=$(Test-Path $fallback)"
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = Launch $file
  Say "(c) launched pid $($p.Id)"
  $dialog = [IntPtr]::Zero
  while ($sw.ElapsedMilliseconds -lt 15000 -and $dialog -eq [IntPtr]::Zero -and -not $p.HasExited) { $dialog = [Dlg]::Dialog([Dlg]::Tree($p.Id)); if ($dialog -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 50 } }
  $dialogAt = $sw.ElapsedMilliseconds
  if ($dialog -eq [IntPtr]::Zero) { Say "(c) NO DIALOG within $dialogAt ms (exited=$($p.HasExited))" } else { Say "(c) dialog visible at $dialogAt ms: $([Dlg]::DialogText($dialog))" }
  while ($sw.ElapsedMilliseconds -lt 6000) { Start-Sleep -Milliseconds 50 }
  Say "(c) at $($sw.ElapsedMilliseconds) ms, dialog still up: $(Read-Tree $p.Id)"
  if ($dialog -ne [IntPtr]::Zero) { [void][Dlg]::Close($dialog); Say "(c) WM_CLOSE posted at $($sw.ElapsedMilliseconds) ms" }
  $closedAt = $sw.ElapsedMilliseconds
  while ($sw.ElapsedMilliseconds -lt ($closedAt + 4000)) { Start-Sleep -Milliseconds 50 }
  $after = Read-Tree $p.Id
  Say "(c) at $($sw.ElapsedMilliseconds) ms, 4 s after WM_CLOSE: $after"
  $pids = @([Dlg]::Tree($p.Id))
  EndTree $pids
  Start-Sleep -Milliseconds 1500
  Say "(c) tree ended at $($sw.ElapsedMilliseconds) ms; still alive: $(@($pids | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }).Count); fallback directory after: exists=$(Test-Path $fallback)"
}
Say 'done'
