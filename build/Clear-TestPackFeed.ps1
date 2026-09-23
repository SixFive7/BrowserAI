# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Clears the SECOND Velopack feed -- the suite's own, never-published one --
    so that a stale test pack can never refuse a release again.

.DESCRIPTION
    `Releases/test-pack/` is not a directory the checklist happens to keep. It
    is a FEED, with the same shape as the shipping one: `.nupkg`s, a
    `releases.<channel>.json`, a legacy `RELEASES` file and an
    `assets.<channel>.json`. Every gate pack writes a pre-release into it, and a
    gate runs far more often than a release is cut.

    SO AT THE MOMENT A RELEASE IS CUT IT HOLDS VERSIONS NEWER THAN THE RELEASE,
    and `vpk` refuses it exactly the way it refuses one in `Releases/`:

        There is a release in channel win which is equal or greater to the
        current version 1.0.0

    Because the running order packs the shipping artifacts FIRST, that refusal
    fires after the real pack has already succeeded -- so the non-zero exit
    names the suite's installer while the release itself is sitting finished on
    disk, which reads as a broken release and not as a dirty scratch
    directory. It refused the 2026-09-16 cut in precisely that shape.

    Q200, decided 2026-09-17: the script clears its own regenerated output
    instead of a checklist item asking a human to remember.

    WHAT IT DELETES IS EXACTLY WHAT THE NEXT PACK REGENERATES, BY NAME. Not the
    directory. A directory wipe would be a wider promise than this step can
    keep -- somebody may have left a note, a log or a downloaded artifact in
    there -- and the thing that must go is specifically the set `vpk` reads when
    it decides whether the version being packed is newer than the feed.

    WHAT IT MUST NEVER TOUCH is the shipping feed above it or `archive/` beside
    it. The first is the release; the second is the only rollback target this
    project keeps. This script is handed the test directory and never derives
    the shipping one, so there is no path by which an argument mistake reaches
    them -- and the suite asserts both survive, byte for byte.

    IT IS A SEPARATE SCRIPT FOR THE SAME REASON Test-ReleaseVersion.ps1 and
    Write-ReleaseManifest.ps1 are: the suite has to be able to DRIVE it. A
    deletion written inline in New-Release.ps1 could only ever be asserted by
    reading the file for a line, which proves the line was typed and not that it
    removes anything.

    NOTHING TO CLEAR IS NOT A FAILURE. The first cut on a machine meets an
    absent directory and the second meets an empty one; a step that refused
    either would refuse every release on a fresh clone.

.PARAMETER Directory
    The test feed's directory -- `Releases/test-pack` under an ordinary cut.
    Absent or empty is fine and is reported, not refused.

.PARAMETER PackId
    The pack id the test artifacts carry (`BrowserAI.app.test`). Passed in
    and not hard-coded, so the id lives in exactly one place:
    New-Release.ps1 hands it its own `$testPackId`.

.PARAMETER DownloadId
    The download id the two renamed artifacts carry (`BrowserAI.test`), for the
    same reason.

.PARAMETER Channel
    The Velopack channel, which names two of the files.

.EXAMPLE
    ./Clear-TestPackFeed.ps1 -Directory ./Releases/test-pack -PackId BrowserAI.app.test -DownloadId BrowserAI.test -Channel win
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Directory,

    [Parameter(Mandatory = $true)]
    [string] $PackId,

    [Parameter(Mandatory = $true)]
    [string] $DownloadId,

    [string] $Channel = 'win'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
    Write-Host "Nothing to clear: $Directory does not exist yet, so the next pack into test-pack starts from an empty feed."
    exit 0
}

# Every name here is one `vpk pack` writes again on the next run. The two
# `-Setup.exe` / `-Portable.zip` entries are the PRE-RENAME names: a run that
# died between the pack and the rename leaves those instead, and `vpk` reads the
# `.nupkg` and not the renamed executable, so either shape refuses the next
# cut and both have to go.
$regenerated = @(
    "$PackId-*.nupkg"
    "releases.$Channel.json"
    'RELEASES'
    "assets.$Channel.json"
    "$DownloadId-installer.exe"
    "$DownloadId-portable.zip"
    "$PackId-$Channel-Setup.exe"
    "$PackId-$Channel-Portable.zip"
)

$removed = @()

foreach ($pattern in $regenerated) {
    # -File: a directory that happens to carry one of these names is not this
    # step's business, and deleting one would need a recursive delete, which is
    # a wider promise than clearing a feed.
    $matched = Get-ChildItem -LiteralPath $Directory -Filter $pattern -File -Force -ErrorAction SilentlyContinue

    foreach ($file in $matched) {
        Remove-Item -LiteralPath $file.FullName -Force
        $removed += $file.Name
    }
}

if ($removed.Count -eq 0) {
    Write-Host "Nothing to clear in $Directory : the test-pack feed holds none of the files the next pack regenerates."
    exit 0
}

Write-Host "Cleared $($removed.Count) file(s) from the suite's own test-pack feed at $Directory so that vpk cannot refuse this cut over a pre-release nobody published: $($removed -join ', ')."
exit 0
