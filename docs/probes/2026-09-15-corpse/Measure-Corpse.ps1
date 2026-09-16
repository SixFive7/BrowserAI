# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
#
# What a run with nobody to serve costs when the launcher's pid is still
# openable -- the shape LauncherCorpse.Openable produces. Scratch only.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$exe = 'C:\Source\SixFive7\BrowserAI\src\BrowserAI\bin\Release\net10.0-windows\win-x64\publish\BrowserAI.Server.exe'
$root = Join-Path $env:LOCALAPPDATA ("BrowserAI-corpse-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$null = New-Item -ItemType Directory -Force -Path $root

$before = $env:BROWSERAI_ROOT
$beforeFirstRun = $env:VELOPACK_FIRSTRUN
$env:BROWSERAI_ROOT = $root
Remove-Item Env:VELOPACK_FIRSTRUN -ErrorAction SilentlyContinue

try {
    $started = [datetime]::UtcNow
    # ONE cmd, and the handle is held by this session for the whole run.
    $launcher = Start-Process -FilePath "$env:SystemRoot\system32\cmd.exe" `
        -ArgumentList '/c', 'start', '/b', '""', "`"$exe`"" `
        -WorkingDirectory $root -WindowStyle Hidden -PassThru
    $launcher.WaitForExit()
    $launcherGone = $launcher.ExitTime.ToUniversalTime()

    $deadline = (Get-Date).AddMinutes(2)
    $log = $null
    while ((Get-Date) -lt $deadline) {
        $log = Get-ChildItem -Path (Join-Path $root 'logs') -Filter 'browserai-*.log' -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($log) {
            $text = [System.IO.File]::ReadAllText($log.FullName)
            if ($text -match 'no client to serve and is exiting') { break }
        }
        Start-Sleep -Milliseconds 20
    }

    $text = [System.IO.File]::ReadAllText($log.FullName)

    $pid2 = [int]([regex]::Match($text, 'pid=(\d+)@').Groups[1].Value)
    $goneAt = $null
    while ((Get-Date) -lt $deadline) {
        if ($null -eq (Get-Process -Id $pid2 -ErrorAction SilentlyContinue)) { $goneAt = [datetime]::UtcNow; break }
        Start-Sleep -Milliseconds 2
    }

    Write-Host "---- launcher started $($started.ToString('o'))"
    Write-Host "---- product pid $pid2 gone by $($goneAt.ToString('o'))"
    Write-Host "---- root: $root"
    Write-Host "---- launcher pid $($launcher.Id), exited $($launcherGone.ToString('o'))"
    Write-Host "---- under the root afterwards: $((Get-ChildItem -Path $root -Force | Select-Object -ExpandProperty Name) -join ', ')"
    Write-Host '---- log ----'
    Write-Host $text
}
finally {
    if ($null -ne $before) { $env:BROWSERAI_ROOT = $before } else { Remove-Item Env:BROWSERAI_ROOT -ErrorAction SilentlyContinue }
    if ($null -ne $beforeFirstRun) { $env:VELOPACK_FIRSTRUN = $beforeFirstRun }
}
