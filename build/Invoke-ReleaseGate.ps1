# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    The PowerShell half of the RELEASE gate: three full runs under
    `BROWSERAI_RELEASE_RUN`, each forcing an UPPER-case drive letter and
    declaring it.

.DESCRIPTION
    RELEASING.md item 8 owns when this is run; TESTING.md owns how. It is
    `Invoke-OrdinaryGate.ps1` three times with the release variable set, and the
    two differences are the whole point:

      - THREE RUNS, because the repetition buys the flake that appears once in
        three. That is how the probe-report race was found on 2026-08-19. On an
        intermediate batch it buys nothing, which is why the ordinary gate is a
        separate file and not a switch.
      - BROWSERAI_RELEASE_RUN=1, which turns every capability skip into a
        failure and makes a filtered run refuse itself from the session hook.

    ⚠️ NOT THE SHARED WRAPPER CLAUDE.md FORBIDS, for the reason
    `Invoke-OrdinaryGate.ps1` gives: the Git Bash half forces the other spelling
    and is a different instrument, and nothing here stands in for it.

    ⚠️ IT STOPS ON THE FIRST CLEARANCE DIFFERENCE and does not run the rest. A
    run that disturbed the maintainer's Add/Remove entry, registration or Start
    Menu shortcut is a run whose successors would be measuring a changed
    machine.

.PARAMETER Runs
    How many. Three is the gate; the parameter exists so a re-run of one can be
    driven without editing this file.

.PARAMETER Prefix
    Names the logs and the clearance snapshots. Defaults to `rel-ps`.

.EXAMPLE
    Start-Process pwsh -WindowStyle Hidden -ArgumentList '-NoProfile','-File','build/Invoke-ReleaseGate.ps1' -RedirectStandardOutput .work/suite/rel-ps-driver.log
#>
[CmdletBinding()]
param([int] $Runs = 3, [string] $Prefix = 'rel-ps')

$ErrorActionPreference = 'Continue'
$PSStyle.OutputRendering = 'PlainText'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# The forced spelling, as an argument and not a `cd` -- see
# Invoke-OrdinaryGate.ps1 for why that is the only place it can be forced.
$forced = $root.Substring(0, 1).ToUpperInvariant() + $root.Substring(1)
$null = New-Item -ItemType Directory -Force -Path (Join-Path $root '.work' 'suite')
$scratch = Join-Path $root '.work' 'test-scratch'

foreach ($n in 1..$Runs) {
    $tag = "$Prefix-$n"
    Write-Host "=== RELEASE RUN $tag starting $(Get-Date -Format HH:mm:ss) ==="

    & (Join-Path $PSScriptRoot 'Get-ClearanceSnapshot.ps1') -Tag "$tag-before" | Out-Null

    $waited = 0
    while ((Get-ChildItem $scratch -Force -ErrorAction SilentlyContinue).Count -gt 0 -and $waited -lt 120) {
        Get-ChildItem $scratch -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        $waited += 2
    }
    Write-Host "scratch released after ${waited}s"

    $log = Join-Path $root '.work' 'suite' "$tag.log"
    $env:BROWSERAI_RELEASE_RUN = '1'
    $env:BROWSERAI_DRIVE_CASE = 'upper'
    & dotnet test (Join-Path $forced 'BrowserAI.slnx') 2>&1 | Tee-Object -LiteralPath $log
    Get-Content (Join-Path $root '.work' 'suite-coverage.txt') -ErrorAction SilentlyContinue | Add-Content -LiteralPath $log

    & (Join-Path $PSScriptRoot 'Get-ClearanceSnapshot.ps1') -Tag "$tag-after" | Out-Null

    $difference = Compare-Object `
        (Get-Content (Join-Path $root '.work' 'clearance' "$tag-before.txt")) `
        (Get-Content (Join-Path $root '.work' 'clearance' "$tag-after.txt"))

    if ($difference) {
        Write-Host "=== CLEARANCE DIFF ON $tag - STOPPING ==="
        $difference | ForEach-Object { '{0} {1}' -f $_.SideIndicator, $_.InputObject } | Write-Host
        Write-Host 'RELEASE-PS-ABORTED-ON-CLEARANCE'
        exit 1
    }

    Write-Host "=== RELEASE RUN $tag done $(Get-Date -Format HH:mm:ss), clearance clean ==="
}

Write-Host 'RELEASE-PS-DONE'
