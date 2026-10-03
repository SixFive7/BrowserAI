# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# One reading of a launched tree, by parent pid from the given root: the members
# running from the browsers root with their --type, the tree's visible windows, its
# Chrome message-only windows and their titles, and the restart registration of the
# browser process (the member whose parent is not itself a browser process).
param([int]$Root)
$S = 'C:\Source\SixFive7\BrowserAI\.work\stale-scratch'
if (-not ('Dlg' -as [type])) { Add-Type -Path (Join-Path $S 'rigs\row32\Dlg.cs') }
$browsers = Join-Path $env:LOCALAPPDATA 'BrowserAI\browsers'
$tree = [Dlg]::Tree($Root)
$members = @()
foreach ($p in $tree) {
  $c = Get-CimInstance Win32_Process -Filter "ProcessId=$p" -ErrorAction SilentlyContinue
  if ($c -and $c.ExecutablePath -and $c.ExecutablePath.StartsWith($browsers, [StringComparison]::OrdinalIgnoreCase)) {
    $type = if ($c.CommandLine -match '--type=(\S+)') { $Matches[1] } else { 'browser' }
    $members += [pscustomobject]@{ pid = [int]$p; parent = [int]$c.ParentProcessId; type = $type }
  }
}
$browserPids = @($members | ForEach-Object { $_.pid })
$roots = @($members | Where-Object { $browserPids -notcontains $_.parent })
[pscustomobject]@{
  root = $Root
  browserProcesses = $members.Count
  types = (($members | Group-Object type | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ',')
  visibleWindows = @([Dlg]::Visible($tree))
  messageWindows = @([Dlg]::MessageWindows($tree))
  restart = @($roots | ForEach-Object { "pid $($_.pid): $([Dlg]::Restart($_.pid))" })
  pids = $browserPids
} | ConvertTo-Json -Compress -Depth 4
