# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

param(
  [Parameter(Mandatory=$true)][string]$ProbeRoot,
  [Parameter(Mandatory=$true)][string]$Out,
  [int]$Seconds = 900
)

$ErrorActionPreference = 'Continue'
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$w  = [System.IO.StreamWriter]::new([System.IO.FileStream]::new($Out,'Create','Write','ReadWrite'))

function Emit($kind, $obj) {
  $line = [ordered]@{ t = [math]::Round($sw.Elapsed.TotalSeconds,3); wall = (Get-Date).ToString('HH:mm:ss.fff'); kind = $kind }
  foreach ($k in $obj.Keys) { $line[$k] = $obj[$k] }
  $w.WriteLine(($line | ConvertTo-Json -Compress -Depth 6))
  $w.Flush()
}

$seenProc = @{}
$seenFile = @{}
$rootPrefix = $ProbeRoot.TrimEnd('\') + '\'

Emit 'start' @{ probeRoot = $ProbeRoot }

while ($sw.Elapsed.TotalSeconds -lt $Seconds) {

  # --- processes whose image lives under the probe root, plus their children
  try {
    $all = Get-CimInstance Win32_Process -ErrorAction Stop
    $ours = @($all | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($rootPrefix, 'OrdinalIgnoreCase') })
    $ourPids = @($ours | ForEach-Object { $_.ProcessId })
    # one generation of children by parent pid, whatever their image
    $kids = @($all | Where-Object { $ourPids -contains $_.ParentProcessId })
    $grandkids = @($all | Where-Object { @($kids | ForEach-Object { $_.ProcessId }) -contains $_.ParentProcessId })
    foreach ($p in @($ours + $kids + $grandkids)) {
      $key = "$($p.ProcessId)|$($p.CreationDate)"
      if ($seenProc.ContainsKey($key)) { continue }
      $seenProc[$key] = $true
      Emit 'cim-proc+' @{
        pid_ = $p.ProcessId; ppid = $p.ParentProcessId; name = $p.Name
        path = $p.ExecutablePath; cmd = $p.CommandLine; created = "$($p.CreationDate)"
      }
    }
  } catch { Emit 'cim-error' @{ message = "$_" } }

  # --- files appearing under the probe root
  if (Test-Path -LiteralPath $ProbeRoot) {
    try {
      $files = Get-ChildItem -LiteralPath $ProbeRoot -Recurse -Force -ErrorAction SilentlyContinue |
               Where-Object { -not $_.PSIsContainer }
      foreach ($f in $files) {
        $rel = $f.FullName.Substring($ProbeRoot.Length).TrimStart('\')
        if (-not $seenFile.ContainsKey($rel)) {
          $seenFile[$rel] = $f.Length
          Emit 'file+' @{ path = $rel; size = $f.Length }
        } elseif ($seenFile[$rel] -ne $f.Length) {
          $seenFile[$rel] = $f.Length
          Emit 'file~' @{ path = $rel; size = $f.Length }
        }
      }
      $total = ($files | Measure-Object -Property Length -Sum).Sum
      Emit 'size' @{ files = @($files).Count; bytes = [int64]$total }
    } catch { Emit 'fs-error' @{ message = "$_" } }
  }

  if (Test-Path -LiteralPath ($Out + '.stop')) { break }
  Start-Sleep -Milliseconds 900
}

Emit 'stop' @{ reason = 'end' }
$w.Flush(); $w.Close()
