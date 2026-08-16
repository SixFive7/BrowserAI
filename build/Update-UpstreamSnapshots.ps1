# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Regenerates the four upstream snapshots and compares them with the
    committed copies.

.DESCRIPTION
    Build-order step 4 -- the tripwire. Four files are regenerated from the
    ASSEMBLED payload on every build and diffed against the committed copies:

        tools-list.json      a tool added, removed, renamed, or its schema changed
        cli-help.txt         a flag that vanished (the --output-mode class)
        config-schema.d.ts   a renamed or removed config key (the SILENT class:
                             loadConfig is a bare JSON.parse with no validation)
        browsers.json        a moved browser revision

    A difference exits non-zero and prints the diff itself. Not "someone should
    look" -- "here is precisely what moved, adjudicate it". The adjudication is
    UPSTREAM-REVIEW.md, and accepting a diff here IS adopting an upstream
    version.

    Generation and comparison are deliberately in different files. The node
    script says what upstream says; this script says whether that matches what
    we committed. One code path doing both could only ever agree with itself.

.PARAMETER Accept
    Overwrite the committed snapshots with what upstream now says, after the
    review in UPSTREAM-REVIEW.md has been done. Prints what it replaced.

.PARAMETER PayloadRoot
    The assembled payload. Built by build/Build-Payload.ps1; gitignored.

.PARAMETER SnapshotRoot
    Where the committed snapshots live.

.PARAMETER ScratchRoot
    Working directory for the regenerated copies and the rendered diff. Under
    .work/, per CLAUDE.md.

.EXAMPLE
    pwsh -File build/Update-UpstreamSnapshots.ps1

.EXAMPLE
    pwsh -File build/Update-UpstreamSnapshots.ps1 -Accept
#>
[CmdletBinding()]
param(
    [switch] $Accept,
    [string] $PayloadRoot = (Join-Path $PSScriptRoot '..' 'payload'),
    [string] $SnapshotRoot = (Join-Path $PSScriptRoot '..' 'upstream-snapshots'),
    [string] $ScratchRoot = (Join-Path $PSScriptRoot '..' '.work' 'upstream-snapshots')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$PSNativeCommandUseErrorActionPreference = $false
# This script's output IS the build's failure message. ANSI colour codes in an
# MSBuild error read as line noise, and PowerShell 7 emits them even when
# redirected unless told otherwise.
$PSStyle.OutputRendering = 'PlainText'

$PayloadRoot = [System.IO.Path]::GetFullPath($PayloadRoot)
$SnapshotRoot = [System.IO.Path]::GetFullPath($SnapshotRoot)
$ScratchRoot = [System.IO.Path]::GetFullPath($ScratchRoot)

# The four, by name. A fifth file appearing in either directory is a
# difference like any other -- see the comparison below.
$snapshots = @('tools-list.json', 'cli-help.txt', 'config-schema.d.ts', 'browsers.json')

$nodeExe = Join-Path $PayloadRoot 'node' 'node.exe'
$generator = Join-Path $PSScriptRoot 'upstream-snapshots.mjs'
$generated = Join-Path $ScratchRoot 'generated'

if (-not (Test-Path -LiteralPath $nodeExe)) {
    throw "No payload at $PayloadRoot. Run build/Build-Payload.ps1 first; the snapshots are generated from what it resolved, and there is nothing honest to compare without it."
}

New-Item -ItemType Directory -Force -Path $ScratchRoot | Out-Null
Remove-Item -LiteralPath $generated -Recurse -Force -ErrorAction SilentlyContinue

& $nodeExe $generator --payload $PayloadRoot --out $generated --scratch (Join-Path $ScratchRoot 'run')
if ($LASTEXITCODE -ne 0) {
    throw "build/upstream-snapshots.mjs exited $LASTEXITCODE."
}

# ---------------------------------------------------------------------------
# Compare
# ---------------------------------------------------------------------------

function Get-FileSha256 {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

# Rendered by git when git is available, because a unified diff is what a
# reviewer can act on. The Compare-Object fallback exists so that a machine
# without git gets a usable answer instead of a confusing one -- the gate is
# about what moved, and it must not be defeated by the absence of a diff tool.
function Get-RenderedDiff {
    param(
        [AllowEmptyString()][string] $Committed = '',
        [Parameter(Mandatory)][string] $Generated)

    if (-not $Committed) {
        return '  (no committed copy exists)'
    }

    # -First 1 is required, not tidiness: Git for Windows puts git.exe on PATH
    # twice (cmd\ and mingw64\bin\), Get-Command returns both, and `$git.Source`
    # on the pair is one string naming two executables.
    $git = Get-Command 'git' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($git) {
        $output = & $git.Source -c 'core.autocrlf=false' diff --no-index --no-color --unified=3 -- $Committed $Generated 2>&1
        if ($output) {
            return ($output | Out-String).TrimEnd()
        }
    }

    $left = Get-Content -LiteralPath $Committed
    $right = Get-Content -LiteralPath $Generated
    return (Compare-Object -ReferenceObject $left -DifferenceObject $right |
        ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" } |
        Out-String).TrimEnd()
}

$differences = @()

foreach ($name in $snapshots) {
    $committedPath = Join-Path $SnapshotRoot $name
    $generatedPath = Join-Path $generated $name

    if (-not (Test-Path -LiteralPath $generatedPath)) {
        throw "The generator produced no $name. That is a defect in build/upstream-snapshots.mjs, not an upstream change."
    }

    if ((Get-FileSha256 $committedPath) -eq (Get-FileSha256 $generatedPath)) {
        continue
    }

    $differences += [pscustomobject]@{
        Name      = $name
        Committed = (Test-Path -LiteralPath $committedPath) ? $committedPath : $null
        Generated = $generatedPath
    }
}

# A file in the snapshot directory that the generator does not produce is a
# stale snapshot, and a stale snapshot is one nobody is diffing.
$unexpected = @(Get-ChildItem -LiteralPath $SnapshotRoot -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notin $snapshots } |
    ForEach-Object { $_.Name })

# ---------------------------------------------------------------------------
# Accept, or fail with the diff
# ---------------------------------------------------------------------------

if ($Accept) {
    New-Item -ItemType Directory -Force -Path $SnapshotRoot | Out-Null
    foreach ($name in $snapshots) {
        Copy-Item -LiteralPath (Join-Path $generated $name) -Destination (Join-Path $SnapshotRoot $name) -Force
    }

    if ($differences.Count -eq 0) {
        Write-Host 'All four snapshots already matched. Nothing was adopted.'
    }
    else {
        Write-Host "Accepted $($differences.Count) changed snapshot(s): $($differences.Name -join ', ')"
        Write-Host 'This is an adoption. UPSTREAM-REVIEW.md governs what has to be recorded in upstream-review.json.'
    }
    exit 0
}

if ($differences.Count -eq 0 -and $unexpected.Count -eq 0) {
    Write-Host "Upstream snapshots match ($($snapshots.Count) files)."
    exit 0
}

$report = [System.Text.StringBuilder]::new()
[void]$report.AppendLine('UPSTREAM SNAPSHOT MISMATCH. What the resolved payload produces is not what is committed.')
[void]$report.AppendLine('')
[void]$report.AppendLine('This is an upstream change, not a stale file to overwrite. Read UPSTREAM-REVIEW.md,')
[void]$report.AppendLine('adjudicate what moved, then: pwsh -File build/Update-UpstreamSnapshots.ps1 -Accept')
[void]$report.AppendLine('')

foreach ($difference in $differences) {
    [void]$report.AppendLine("--- $($difference.Name) ---")
    [void]$report.AppendLine((Get-RenderedDiff -Committed $difference.Committed -Generated $difference.Generated))
    [void]$report.AppendLine('')
}

foreach ($name in $unexpected) {
    [void]$report.AppendLine("--- $name ---")
    [void]$report.AppendLine('  Present in upstream-snapshots/ and produced by nothing. Nothing diffs it, so it is not a snapshot.')
    [void]$report.AppendLine('')
}

$reportPath = Join-Path $ScratchRoot 'diff.txt'
Set-Content -LiteralPath $reportPath -Value $report.ToString() -Encoding utf8NoBOM
[void]$report.AppendLine("Full diff also written to $reportPath")

Write-Output $report.ToString()
exit 1
