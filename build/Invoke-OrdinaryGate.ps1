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

    ⚠️ THIS IS NOT THE SHARED WRAPPER SCRIPT AGENTS.md FORBIDS. That rule is
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

# ⚠️ A RUN WRITES TO A FILE, AND THIS DRIVER WAITS FOR THE RUN'S PROCESS -- 64 c, the
# maintainer's words verbatim: "64 c now". Until 2026-10-11 a run was read through a
# pipe into Tee-Object, and a pipe ends only once every process holding its write end
# has closed it. Twice that day a frozen child of a test host held it after the run had
# printed its summary, a suspended git (pid 102292) and a suspended cmd.exe (pid
# 23920), and the driver waited on them with nothing in any log to say why. The run's
# cmd.exe opens the run's log and hands that FILE to `dotnet test`, so a child that
# inherits it holds nothing up; the driver waits for the cmd.exe it started, and the
# functions below copy what the run wrote into the driver's own output as the file
# grows. GateDriverTests runs each driver against a run that leaves such a child behind.
function Copy-Grown([string] $Path, [long] $From) {
    if (-not (Test-Path -LiteralPath $Path)) { return $From }
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
    try {
        $grown = $stream.Length - $From
        if ($grown -le 0) { return $From }
        $bytes = [byte[]]::new($grown)
        $null = $stream.Seek($From, [System.IO.SeekOrigin]::Begin)
        $read = $stream.Read($bytes, 0, $bytes.Length)
        [Console]::Out.Flush()
        $out = [Console]::OpenStandardOutput()
        $out.Write($bytes, 0, $read)
        $out.Flush()
        return $From + $read
    }
    finally {
        $stream.Dispose()
    }
}

# Waits for the run's process, never for the end of a pipe, copying the run's log as
# it grows; then copies the rest.
function Wait-Run([System.Diagnostics.Process] $Run, [string] $Log) {
    $copied = 0L
    while (-not $Run.HasExited) {
        $copied = Copy-Grown -Path $Log -From $copied
        Start-Sleep -Milliseconds 500
    }
    $null = Copy-Grown -Path $Log -From $copied
}

# Appends one file to a run's log while a child the run left behind may still hold the
# log open: shared for reading, writing and deletion, as the run's own handle is.
function Add-Shared([string] $Path, [string] $Source) {
    if (-not (Test-Path -LiteralPath $Source)) { return }
    $bytes = [System.IO.File]::ReadAllBytes($Source)
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}

# The run itself, through cmd.exe, whose redirection opens the log and hands its handle
# to the run for both of its output streams.
function Start-Run([string] $Command, [string] $Log) {
    $start = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32' 'cmd.exe'))
    $start.Arguments = '/d /s /c "' + $Command + ' > "' + $Log + '" 2>&1"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    return [System.Diagnostics.Process]::Start($start)
}

# ⚠️ EVERY FOLDER UNDER .work\kept\ GOES INTO THE RUN'S RECORDS, BESIDE ITS LOG -- 119, the
# maintainer's words verbatim: "119 your recommendation". A red arm moves its scratch folder
# there (tests/BrowserAI.Tests/Harness/KeptOnRed.cs), where neither the reclaim at the head
# of a run nor this driver's clearing of the scratch reaches it, and until 2026-10-12
# nothing collected it. After the run and before the clearance comparison, each one is
# copied into .work\suite\<tag>-kept\, made fresh for the run, and named in the run's log
# and in this driver's output, with whether this run kept it. Nothing deletes a kept
# folder, so a later run copies it again. GateDriverTests runs each driver over three.
function Get-Kept([string] $Kept) {
    if (-not (Test-Path -LiteralPath $Kept)) { return @() }
    return @(Get-ChildItem -LiteralPath $Kept -Directory -Force | ForEach-Object { $_.Name } | Sort-Object)
}

function Copy-Kept([string] $Root, [string] $Tag, [string[]] $Before, [string] $Log) {
    $kept = Join-Path $Root '.work' 'kept'
    $records = Join-Path '.work' 'suite' "$Tag-kept"
    $names = @(Get-Kept $kept)
    $said = [System.Collections.Generic.List[string]]::new()

    if ($names.Count -eq 0) {
        $said.Add("kept folders: none under $(Join-Path '.work' 'kept')")
    }
    else {
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $Root $records)
        foreach ($name in $names) {
            $when = if ($Before -contains $name) { 'kept before this run' } else { 'kept during this run' }
            $failed = $null
            Copy-Item -LiteralPath (Join-Path $kept $name) -Destination (Join-Path $Root $records) -Recurse -Force -ErrorAction SilentlyContinue -ErrorVariable failed
            if ($failed) {
                $said.Add("kept folder $name, $when, could not be copied whole to $(Join-Path $records $name): $($failed[0].Exception.Message)")
            }
            else {
                $said.Add("kept folder copied: $name, $when, to $(Join-Path $records $name)")
            }
        }
    }

    $said | Write-Host
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($said -join "`n") + "`n")
    $stream = [System.IO.File]::Open($Log, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}

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

# ⚠️ THE SUITE'S INSTALLER, PACKED FROM THIS TREE UNDER THE LOCK AND BEFORE THE
# FIRST RUN -- Q287, the maintainer's words verbatim: "Q287 a". The real-installer
# arms install Releases\test-pack, which only a release cut used to write, so
# they ran the last release's hooks. `-TestPackOnly` packs it from the two
# publishes this run tests, refuses a publish that is not the tree's version,
# and touches nothing a release is made of; the release installer capability
# refuses a pack that is not those bytes. Publish the slice before starting a
# gate: this reads the publish and does not make it.
$packLog = Join-Path $root '.work' 'suite' "$Tag-testpack.log"
# ⚠️ IN A PWSH OF ITS OWN, as the Git Bash drivers run it -- added 2026-10-11. The
# release script stops with a terminating error, and run here with `&` that error
# ended this driver as well, before the test of the exit code below and outside the
# `finally` that lets the lock go: the log had no abort line, and the lock named a pid
# that was gone until the next take found it stale.
& pwsh -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'New-Release.ps1') -TestPackOnly *> $packLog
if ($LASTEXITCODE -ne 0) {
    Get-Content -LiteralPath $packLog -Tail 20 | Write-Host
    & (Join-Path $PSScriptRoot 'InstallerLock.ps1') -Release -HolderPid $PID | Out-Null
    Write-Host 'ORDINARY-PS-ABORTED-ON-TEST-PACK'
    exit 1
}
Get-Content -LiteralPath $packLog | Where-Object { $_ -match '^vpk pack of .* took' } | Write-Host

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

    # The kept folders there before the run, and the run's copy of them made fresh, 119.
    $keptBefore = @(Get-Kept (Join-Path $root '.work' 'kept'))
    Remove-Item -LiteralPath (Join-Path $root '.work' 'suite' "$Tag-kept") -Recurse -Force -ErrorAction SilentlyContinue

    $log = Join-Path $root '.work' 'suite' "$Tag.log"
    $env:BROWSERAI_DRIVE_CASE = 'upper'
    Wait-Run -Run (Start-Run -Command ('dotnet test "' + (Join-Path $forced 'BrowserAI.slnx') + '"') -Log $log) -Log $log

    # The coverage block reaches `.work\suite-coverage.txt` and never a `dotnet
    # test` log: neither real stream survives the MTP integration. Appending it is
    # what gives a multi-run gate one block per run instead of one in total.
    Add-Shared -Path $log -Source (Join-Path $root '.work' 'suite-coverage.txt')
    Copy-Kept -Root $root -Tag $Tag -Before $keptBefore -Log $log

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
