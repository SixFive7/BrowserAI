# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Ends the 55 a measurement: stops the probe's processes, takes the probe's key
    back, analyses the run and says what is left.

.DESCRIPTION
    1. stop.flag in the run folder, which every probe process watches; nothing is
       killed. Each process start.ps1 recorded is then looked up by its pid AND its
       start time, never by name, and must be gone within 15 s.
    2. Every handler a browser started is looked up the same way, by the pid its log
       names and the start time its first line records.
    3. The probe's unregister, which removes HKCU\Software\Classes\browserai-probe
       only when its command still names this probe.
    4. The snapshot again, and the difference in Firefox's Launcher value names.
    5. The analysis, written to report.txt in the run folder.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$exe = Join-Path $here 'bin\LinkProbe.exe'
$run = [IO.File]::ReadAllText((Join-Path $here 'bin\current-run.txt')).Trim()

[IO.File]::WriteAllText((Join-Path $run 'stop.flag'), 'measurement over')

function Find-Alive([int] $id, [DateTime] $startedUtc) {
    $process = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($process -and [Math]::Abs(($process.StartTime.ToUniversalTime() - $startedUtc).TotalSeconds) -lt 2) { return $process }
    return $null
}

$recorded = @()
if (Test-Path (Join-Path $run 'processes.json')) {
    $recorded += @(Get-Content -LiteralPath (Join-Path $run 'processes.json') -Raw | ConvertFrom-Json | ForEach-Object {
        [pscustomobject]@{ Pid = [int] $_.Pid; Started = [DateTime]::Parse($_.Started, $null, 'RoundtripKind').ToUniversalTime(); What = $_.Arguments }
    })
}

foreach ($log in @(Get-ChildItem -LiteralPath $run -Filter 'handle-*.jsonl') + @(Get-ChildItem -LiteralPath $run -Filter 'standin-*.jsonl')) {
    $first = Get-Content -LiteralPath $log.FullName -TotalCount 1 | ConvertFrom-Json
    $recorded += [pscustomobject]@{ Pid = [int] $first.pid; Started = [DateTime]::Parse($first.t, $null, 'RoundtripKind').ToUniversalTime(); What = $first.mode }
}

$deadline = (Get-Date).AddSeconds(15)
do {
    $alive = @($recorded | Where-Object { Find-Alive $_.Pid $_.Started })
    if ($alive.Count -eq 0) { break }
    Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $deadline)

$unregister = Start-Process -FilePath $exe -ArgumentList 'unregister' -Wait -PassThru

$lines = @()
$launcher = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Mozilla\Firefox\Launcher')
$names = if ($launcher) { @($launcher.GetValueNames() | Sort-Object) } else { @() }
if ($launcher) { $launcher.Dispose() }
$lines += "HKCU\Software\Mozilla\Firefox\Launcher value names: $($names.Count)"
$lines += $names | ForEach-Object { "  $_" }
$probe = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\browserai-probe')
$lines += 'HKCU\Software\Classes\browserai-probe: ' + $(if ($probe) { 'PRESENT' } else { 'ABSENT' })
if ($probe) { $probe.Dispose() }
[IO.File]::WriteAllLines((Join-Path $run 'snapshot-after.txt'), [string[]] $lines)

$before = @(Get-Content -LiteralPath (Join-Path $run 'snapshot-before.txt') | Where-Object { $_ -like '  *' })
$after = @($names | ForEach-Object { "  $_" })
$added = @($after | Where-Object { $before -notcontains $_ })
$removed = @($before | Where-Object { $after -notcontains $_ })

$null = Start-Process -FilePath $exe -ArgumentList @('analyze', $run) -Wait -PassThru

"Run folder: $run"
"Processes still alive after 15 s: $($alive.Count)"
$alive | Format-Table -AutoSize | Out-String
"unregister exit code: $($unregister.ExitCode) (0 is removed or absent)"
$lines[-1]
"Firefox Launcher values added: $($added.Count)"
$added
"Firefox Launcher values removed: $($removed.Count)"
$removed
"Report: $(Join-Path $run 'report.txt')"
