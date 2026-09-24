# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    The PowerShell half of the ORDINARY gate: one full run, forcing an
    UPPER-case drive letter and declaring it.

.DESCRIPTION
    TESTING.md owns the invocation; this is that invocation, kept in the tree
    instead of retyped from it. The two halves of a gate are TWO INSTRUMENTS
    and not redundancy: this one hands the test host `C:\...` and declares
    `upper`, the Git Bash half hands it `c:\...` and declares `lower`, and
    `SuiteCoverageTests.TheRunReportsTheDriveLetterSpellingItActuallyReceived`
    fails a run that did not receive what it declared.

    ⚠️ THIS IS NOT THE SHARED WRAPPER SCRIPT CLAUDE.md FORBIDS. That rule is
    about one script standing in for both halves, which would run one instrument
    twice and report what two report. There are four gate scripts here, two per
    shell, and each forces and declares its own spelling; the difference between
    them is the thing being preserved.

    THREE RUNS PER SHELL IS THE RELEASE GATE AND THIS IS NOT ONE.
    `Invoke-ReleaseGate.ps1` is that. `BROWSERAI_RELEASE_RUN` is deliberately
    NOT set here: it would make the run's own coverage block say `release run
    YES`, which an ordinary run is not.

    ⚠️ THE RUN IS DETACHED BY THE CALLER, NOT BY THIS SCRIPT. A grandchild that
    inherits the caller's stdout handle keeps the pipe open after the command
    itself has exited, so the caller never sees EOF and its timeout does not
    fire. Start this with `Start-Process ... -RedirectStandardOutput` and poll
    the file; TESTING.md's "How the suite is run" owns that half.

    ⚠️ AND READ THE DRIVER'S OWN LOG FOR ITS `starting` LINE BEFORE WAITING ON
    IT. On 2026-09-22 at 19:26 a driver whose script did not exist died in
    milliseconds and read exactly like one that was working; fourteen minutes
    were lost to waiting on it. That is also why these files are in the tree at
    all and not recreated from prose every session.

.PARAMETER Tag
    Names the log and the clearance snapshots. Defaults to `ord-ps-1`.

.EXAMPLE
    Start-Process pwsh -WindowStyle Hidden -ArgumentList '-NoProfile','-File','build/Invoke-OrdinaryGate.ps1' -RedirectStandardOutput .work/suite/ord-ps-1-driver.log
#>
[CmdletBinding()]
param([string] $Tag = 'ord-ps-1')

# Continue and not Stop: a red run is data, and this script's job is to
# record it and compare the clearance, not to abort on the first non-zero exit.
$ErrorActionPreference = 'Continue'
$PSStyle.OutputRendering = 'PlainText'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# ⚠️ THE FORCED SPELLING, AND IT IS AN ARGUMENT, NOT A `cd`. An
# explicitly-spelled absolute path lands in MSBuildProjectDirectory, in
# TargetPath and therefore in the test host's own AppContext.BaseDirectory,
# whatever the working directory says. A `cd` cannot reach it: Windows always
# answers upper for a resolved path.
$forced = $root.Substring(0, 1).ToUpperInvariant() + $root.Substring(1)
$null = New-Item -ItemType Directory -Force -Path (Join-Path $root '.work' 'suite')

Write-Host "=== ORDINARY RUN $Tag starting $(Get-Date -Format HH:mm:ss) ==="

# ⚠️ THE INSTALLER LOCK, BEFORE THE FIRST CLEARANCE SNAPSHOT AND LET GO AFTER THE
# LAST -- Q291, the maintainer's words verbatim: "Q291 a". The suite takes
# .work\installer.lock itself when a session starts; this driver takes it first,
# for its own pid, and declares the token so the test host it starts finds the
# holder it was told about instead of waiting for it. A live holder is waited
# for, and a gate that could not take it runs nothing.
$token = & (Join-Path $PSScriptRoot 'InstallerLock.ps1') -Take -HolderPid $PID
if ($LASTEXITCODE -ne 0) {
    Write-Host 'ORDINARY-PS-ABORTED-ON-LOCK'
    exit 1
}
$env:BROWSERAI_INSTALLER_LOCK_HELD = $token
Write-Host "installer lock held: $token"

$outcome = 'ORDINARY-PS-DONE'

try {
    & (Join-Path $PSScriptRoot 'Get-ClearanceSnapshot.ps1') -Tag "$Tag-before" | Out-Null

    # ⚠️ WAIT FOR THE RIG TREE TO BE RELEASED, NOT FOR THE PREVIOUS RUN TO REPORT. A
    # test host that has printed its summary has not necessarily let go of its
    # handles: on the 2026-09-15 release gate 137 rig directories were still held
    # after run 1 reported. The piped form is used because the harness path guard
    # refuses a wildcard argument to Remove-Item.
    $scratch = Join-Path $root '.work' 'test-scratch'
    $waited = 0
    while ((Get-ChildItem $scratch -Force -ErrorAction SilentlyContinue).Count -gt 0 -and $waited -lt 120) {
        Get-ChildItem $scratch -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        $waited += 2
    }
    Write-Host "scratch released after ${waited}s"

    $log = Join-Path $root '.work' 'suite' "$Tag.log"
    $env:BROWSERAI_DRIVE_CASE = 'upper'
    & dotnet test (Join-Path $forced 'BrowserAI.slnx') 2>&1 | Tee-Object -LiteralPath $log

    # The coverage block reaches `.work\suite-coverage.txt` and never a `dotnet
    # test` log: neither real stream survives the MTP integration. Appending it is
    # what gives a multi-run gate one block per run instead of one in total.
    Get-Content (Join-Path $root '.work' 'suite-coverage.txt') -ErrorAction SilentlyContinue | Add-Content -LiteralPath $log

    & (Join-Path $PSScriptRoot 'Get-ClearanceSnapshot.ps1') -Tag "$Tag-after" | Out-Null

    $difference = Compare-Object `
        (Get-Content (Join-Path $root '.work' 'clearance' "$Tag-before.txt")) `
        (Get-Content (Join-Path $root '.work' 'clearance' "$Tag-after.txt"))

    if ($difference) {
        Write-Host "=== CLEARANCE DIFF ON $Tag - STOPPING ==="
        $difference | ForEach-Object { '{0} {1}' -f $_.SideIndicator, $_.InputObject } | Write-Host
        $outcome = 'ORDINARY-PS-ABORTED-ON-CLEARANCE'
    }
    else {
        Write-Host "=== ORDINARY RUN $Tag done $(Get-Date -Format HH:mm:ss), clearance clean ==="
    }
}
finally {
    & (Join-Path $PSScriptRoot 'InstallerLock.ps1') -Release -HolderPid $PID | Out-Null
}

Write-Host $outcome
if ($outcome -ne 'ORDINARY-PS-DONE') { exit 1 }
