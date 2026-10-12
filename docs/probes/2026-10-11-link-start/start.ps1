# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Starts the 55 a measurement. Run it only on the maintainer's go; stop.ps1 undoes it.

.DESCRIPTION
    1. A new run folder, run-<UTC stamp>, which bin\current-run.txt then names.
    2. A snapshot of what a Firefox run could touch: the value names under
       HKCU\Software\Mozilla\Firefox\Launcher, and whether the probe's key exists.
    3. HKCU\Software\Classes\browserai-probe, written by the probe itself (the one
       write this run was allowed). It refuses to write over a key that exists.
    4. The folders the links open, inside the run folder.
    5. Three processes of the probe, none with a window: the observer and the two
       page servers on 127.0.0.1. The observer starts the fourth, the stand-in
       background, the first time a browser holds the foreground, so that it is not
       made while the editor this script runs under is in front (a process made
       then may raise a window on its own, which a task-started background cannot).
       Each ends by itself when stop.flag appears in the run folder, or after 45
       minutes.

    It starts nothing that shows a window. The windows the measurement opens are
    opened by the maintainer's own clicks.

.PARAMETER Port1
    The first page's port. Taken by something else, the server takes one Windows picks.

.PARAMETER Port2
    The second page's port, likewise.
#>
[CmdletBinding()]
param(
    [int] $Port1 = 53117,
    [int] $Port2 = 53118
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$exe = Join-Path $here 'bin\LinkProbe.exe'

if (-not (Test-Path -LiteralPath $exe)) { throw "No probe at $exe; build it first." }

$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$run = Join-Path $here "run-$stamp"
$null = New-Item -ItemType Directory -Path $run
[IO.File]::WriteAllText((Join-Path $here 'bin\current-run.txt'), $run)

function Write-Snapshot([string] $tag) {
    $lines = @()
    $launcher = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Mozilla\Firefox\Launcher')
    $names = if ($launcher) { @($launcher.GetValueNames() | Sort-Object) } else { @() }
    if ($launcher) { $launcher.Dispose() }
    $lines += "HKCU\Software\Mozilla\Firefox\Launcher value names: $($names.Count)"
    $lines += $names | ForEach-Object { "  $_" }
    $probe = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\browserai-probe')
    $lines += 'HKCU\Software\Classes\browserai-probe: ' + $(if ($probe) { 'PRESENT' } else { 'ABSENT' })
    if ($probe) { $probe.Dispose() }
    [IO.File]::WriteAllLines((Join-Path $run "snapshot-$tag.txt"), [string[]] $lines)
}

Write-Snapshot 'before'

$register = Start-Process -FilePath $exe -ArgumentList 'register' -Wait -PassThru
if ($register.ExitCode -ne 0) { throw "register exited $($register.ExitCode); its log is in $run. Nothing else was started." }

$prepare = Start-Process -FilePath $exe -ArgumentList 'prepare' -Wait -PassThru
if ($prepare.ExitCode -ne 0) { throw "prepare exited $($prepare.ExitCode); run stop.ps1 to take the key back." }

$started = @()
foreach ($arguments in @(@('observe'), @('serve', 'p1', "$Port1"), @('serve', 'p2', "$Port2"))) {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru
    $started += [pscustomobject]@{
        Pid = $process.Id
        Started = $process.StartTime.ToUniversalTime().ToString('o')
        Arguments = ($arguments -join ' ')
    }
}

$started | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'processes.json') -Encoding utf8

$deadline = (Get-Date).AddSeconds(15)
while ((Get-Date) -lt $deadline -and -not ((Test-Path (Join-Path $run 'serve-p1.port')) -and (Test-Path (Join-Path $run 'serve-p2.port')))) {
    Start-Sleep -Milliseconds 200
}

$p1 = Get-Content -LiteralPath (Join-Path $run 'serve-p1.port') -ErrorAction SilentlyContinue
$p2 = Get-Content -LiteralPath (Join-Path $run 'serve-p2.port') -ErrorAction SilentlyContinue
if (-not $p1 -or -not $p2) { throw "A page server did not start within 15 s; run stop.ps1. Logs: $run" }

$urls = @(
    "Page 1:  http://127.0.0.1:$p1/t1/",
    "Page 2:  http://127.0.0.1:$p2/t1/",
    "Page 1b: http://127.0.0.1:$p1/t2/"
)
[IO.File]::WriteAllLines((Join-Path $run 'urls.txt'), [string[]] $urls)

"Run folder: $run"
$started | Format-Table -AutoSize | Out-String
$urls
if ("$p1" -ne "$Port1" -or "$p2" -ne "$Port2") { 'THE PORTS DIFFER FROM THE STEPS: give him the three addresses above.' }
