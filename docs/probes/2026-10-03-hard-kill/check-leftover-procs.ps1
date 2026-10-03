# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Read-only: are any of the processes this rig recorded (root node per run, the page server per plan)
# still alive with the same start time? Checks by recorded pid + creation time only.
Add-Type -Path 'C:\Source\SixFive7\BrowserAI\.work\hard-kill\rig\hk.cs'
$alive = @(); $checked = 0
foreach ($f in Get-ChildItem 'C:\Source\SixFive7\BrowserAI\.work\hard-kill\results\*.jsonl') {
  foreach ($line in Get-Content -LiteralPath $f.FullName) {
    try { $o = $line | ConvertFrom-Json -AsHashtable } catch { continue }
    if (-not $o.rootPid) { continue }
    $checked++
    $h = [HkWin]::Open([uint32]$o.rootPid)
    if ($h) { if (-not [HkWin]::HasExited($h.H) -and $h.Created -eq [long]$o.rootCreated) { $alive += "$($o.id) pid=$($o.rootPid)" }; [void][HkWin]::CloseHandle($h.H) }
  }
}
"recorded roots checked: $checked; still alive with the recorded start time: $($alive.Count)"
$alive
