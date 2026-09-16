# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

param(
  [string]$Tag = 'chromium',
  [string]$Cfg = 'cfg-chromium.json',
  [switch]$SelfJob,
  [string]$BrowsersPath = '',
  [int]$Settle = 6000,
  [int]$TimeoutSec = 240,
  [string]$Extra = '',
  [switch]$Race,
  [switch]$SelfJobSilent,
  [switch]$SelfJobBreakawayOk,
  [switch]$Breakaway,
  [switch]$NoSuspend,
  [switch]$JobList,
  [switch]$InheritableJob,
  [switch]$CleanupSurvivors,
  [int]$AssignDelay = 0,
  [string]$CliOverride = ''
)
$ErrorActionPreference = 'Stop'
$root = 'C:\Source\SixFive7\BrowserAI\.work\jobtest'
Set-Location $root

$env:PLAYWRIGHT_SKIP_BROWSER_GC = '1'
if ($BrowsersPath -ne '') { $env:PLAYWRIGHT_BROWSERS_PATH = $BrowsersPath } else { Remove-Item Env:PLAYWRIGHT_BROWSERS_PATH -ErrorAction SilentlyContinue }

foreach ($f in @("$Tag.done", "$Tag.report.tsv", "$Tag.pids.tsv", "$Tag.log")) {
  if (Test-Path $f) { Remove-Item $f -Force }
}

$node = (Get-Command node).Source
$cli  = if ($CliOverride -ne '') { (Resolve-Path (Join-Path $root $CliOverride)).Path } else { Join-Path $root '..\probe2\node_modules\@playwright\mcp\cli.js' | Resolve-Path | Select-Object -ExpandProperty Path }
$cfgP = Join-Path $root $Cfg

$q = { param($s) '"' + $s + '"' }
$argList = @('--node', (& $q $node), '--cli', (& $q $cli), '--cfg', (& $q $cfgP), '--outdir', (& $q $root), '--tag', $Tag, '--settle', "$Settle")
if ($SelfJob) { $argList += '--selfjob' }
if ($SelfJobSilent) { $argList += '--selfjobsilent' }
if ($SelfJobBreakawayOk) { $argList += '--selfjobbreakawayok' }
if ($Breakaway) { $argList += '--breakaway' }
if ($Extra -ne '') { $argList += @('--extra', (& $q $Extra)) }
if ($Race) { $argList += '--race' }
if ($NoSuspend) { $argList += '--nosuspend' }
if ($JobList) { $argList += '--joblist' }
if ($InheritableJob) { $argList += '--inheritablejob' }
if ($AssignDelay -gt 0) { $argList += @('--assigndelay', "$AssignDelay") }

$p = Start-Process -FilePath (Join-Path $root 'JobProbe.exe') -ArgumentList $argList -PassThru -WindowStyle Hidden
$probePid = $p.Id
$probeStart = $p.StartTime
"PROBE_PID=$probePid"

$deadline = (Get-Date).AddSeconds($TimeoutSec)
while (-not (Test-Path "$Tag.done")) {
  if ($p.HasExited) { "PROBE EXITED EARLY code=$($p.ExitCode)"; Get-Content "$Tag.log" -ErrorAction SilentlyContinue; exit 1 }
  if ((Get-Date) -gt $deadline) { "TIMEOUT waiting for $Tag.done"; Get-Content "$Tag.log" -ErrorAction SilentlyContinue | Select-Object -Last 60; Stop-Process -Id $probePid -Force -ErrorAction SilentlyContinue; exit 1 }
  Start-Sleep -Milliseconds 400
}

"===== REPORT ====="
Get-Content "$Tag.report.tsv"

# Record (pid, creationFileTime) for every process we spawned. PID-only; never image names.
$recorded = @()
foreach ($line in (Get-Content "$Tag.pids.tsv")) {
  if ($line.Trim() -eq '') { continue }
  $parts = $line -split "`t"
  $recorded += [pscustomobject]@{ Pid = [int]$parts[0]; Created = [long]$parts[1]; Name = $parts[2] }
}
"===== SPAWNED PIDS: $($recorded.Count) ====="
"===== PROCESS TYPES (by PID lookup only) ====="
foreach ($r in $recorded) {
  $ci = Get-CimInstance Win32_Process -Filter "ProcessId=$($r.Pid)" -ErrorAction SilentlyContinue
  $cl = if ($ci) { $ci.CommandLine } else { '<gone>' }
  if ($null -eq $cl) { $cl = '<null>' }
  $type = ([regex]::Match($cl, '--type=([a-zA-Z0-9\-_]+)')).Groups[1].Value
  if ($type -eq '') { $type = ([regex]::Match($cl, '\s-(contentproc|forkserver)\b')).Groups[1].Value }
  if ($type -eq '') { $type = '(main)' }
  $svc = ([regex]::Match($cl, '--utility-sub-type=([a-zA-Z0-9\.\-_]+)')).Groups[1].Value
  $gecko = ([regex]::Match($cl, '-childID\s+(\d+)|\s(tab|gpu|rdd|socket|utility)\s*$')).Groups[0].Value
  $flat = ($cl -replace '\s+', ' ')
  $ns = if ($flat -match '--no-sandbox') { 'HAS--no-sandbox' } else { 'sandboxed' }
  "{0,-7} {1,-22} {2,-16} {3} {4}" -f $r.Pid, $type, $ns, $svc, $gecko
  Add-Content -Path "$Tag.cmdlines.txt" -Value ("PID $($r.Pid) [$type] $flat`n")
}
"(full command lines written to $Tag.cmdlines.txt)"

# --- HARD KILL of the probe (simulates BrowserAI crashing). No graceful shutdown.
"KILLING probe pid=$probePid (TerminateProcess)"
Stop-Process -Id $probePid -Force
Start-Sleep -Seconds 4

# --- Verify every recorded PID is gone. A live PID only counts as survivor if its
#     creation time matches what we recorded (guards against PID reuse).
$survivors = @()
foreach ($r in $recorded) {
  $proc = Get-Process -Id $r.Pid -ErrorAction SilentlyContinue
  if ($null -eq $proc) { continue }
  $recordedStart = [DateTime]::FromFileTimeUtc($r.Created)
  $liveStart = $proc.StartTime.ToUniversalTime()
  if ([Math]::Abs(($liveStart - $recordedStart).TotalSeconds) -lt 2) {
    $survivors += [pscustomobject]@{ Pid = $r.Pid; Name = $r.Name; Started = $liveStart }
  }
}
# also check the probe itself
$probeAlive = $null -ne (Get-Process -Id $probePid -ErrorAction SilentlyContinue)

"===== AFTER KILL ====="
"probe alive: $probeAlive"
if ($survivors.Count -eq 0) { "SURVIVORS: NONE - all $($recorded.Count) spawned processes are gone" }
else {
  "SURVIVORS: $($survivors.Count)"; $survivors | Format-Table -AutoSize | Out-String
  if ($CleanupSurvivors) {
    "CLEANUP: terminating survivors by recorded PID"
    foreach ($s in $survivors) { Stop-Process -Id $s.Pid -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
    $still = @()
    foreach ($s in $survivors) { if (Get-Process -Id $s.Pid -ErrorAction SilentlyContinue) { $still += $s.Pid } }
    if ($still.Count -eq 0) { "CLEANUP OK: all survivors terminated" } else { "CLEANUP FAILED, still alive: $($still -join ',')" }
  }
}
