# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

param([Parameter(Mandatory=$true)][string]$Out, [int]$Seconds = 900)
$ErrorActionPreference = 'Stop'
$stop = $Out + '.stop'
$roots = @("$env:LOCALAPPDATA/BrowserAI.app", "$env:LOCALAPPDATA/BrowserAI")
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$seen = @{}
$fs = New-Object System.IO.FileStream($Out, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
$w = New-Object System.IO.StreamWriter($fs)
function Emit([hashtable]$o) {
  $o['t'] = [math]::Round($sw.Elapsed.TotalSeconds, 3)
  $o['wall'] = (Get-Date).ToString('HH:mm:ss.fff')
  $w.WriteLine(([pscustomobject]$o | ConvertTo-Json -Compress -Depth 4))
  $w.Flush()
}
Emit @{ kind = 'start'; roots = $roots }
while ($sw.Elapsed.TotalSeconds -lt $Seconds -and -not (Test-Path $stop)) {
  foreach ($r in $roots) {
    if (-not (Test-Path $r)) { continue }
    if (-not $seen.ContainsKey($r)) { $seen[$r] = 1; Emit @{ kind = 'root+'; path = (Resolve-Path $r).Path } }
    foreach ($e in (Get-ChildItem $r -Force -ErrorAction SilentlyContinue)) {
      $k = $e.FullName
      if ($seen.ContainsKey($k)) { continue }
      $seen[$k] = 1
      Emit @{ kind = 'entry+'; path = $k; dir = [bool]$e.PSIsContainer; len = $(if ($e.PSIsContainer) { 0 } else { $e.Length }) }
    }
  }
  Start-Sleep -Milliseconds 200
}
Emit @{ kind = 'stop'; reason = 'end' }
$w.Flush(); $w.Close(); $fs.Close()
