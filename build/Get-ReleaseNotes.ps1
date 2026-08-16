# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Extracts the unreleased section of CHANGELOG.md, refuses an empty one, and
    optionally stamps it under the version being cut.

.DESCRIPTION
    Build-order step 18, and the enforcement behind pre-release item 10:
    REFUSE TO RELEASE ON AN EMPTY UNRELEASED SECTION. A release with nothing to
    say is a release nobody can describe afterwards, and the first thing a
    rollback needs is a statement of what changed.

    Empty means NO LIST ITEMS, not "no characters". A section holding nothing
    but its own `### Added` subheads is exactly what a changelog reconstructed
    under release pressure looks like at the moment the pressure starts, and it
    would satisfy a check for non-empty text.

    Three things this deliberately does not do:

      * It does not write the version. The version is derived from the git tag
        by the build (plan/stack.md), so this script is TOLD what is being cut
        and never works it out. Two mechanisms deriving one version is how they
        come to disagree.

      * It does not create the tag, publish anything, or decide that a release
        happens. plan/pre-release.md item 14 is a human.

      * It does not touch a released section. Stamping inserts a new heading
        below `## [Unreleased]` and moves nothing, so the unreleased entries
        become that version's by position rather than by an edit that could
        drop one.

.PARAMETER Path
    The changelog. Defaults to CHANGELOG.md at the repository root.

.PARAMETER StampVersion
    The version being cut, bare and without the tag's `v`. Given, the section
    below `## [Unreleased]` is headed with it after the notes are extracted, so
    a refusal happens before the file is touched.

.PARAMETER Date
    The release date for a stamped heading, ISO 8601. Defaults to today. A
    parameter so a test can assert on the whole line rather than on a prefix.

.EXAMPLE
    pwsh -File build/Get-ReleaseNotes.ps1

.EXAMPLE
    pwsh -File build/Get-ReleaseNotes.ps1 -StampVersion 0.2.0
#>
[CmdletBinding()]
param(
    [string] $Path = (Join-Path $PSScriptRoot '..' 'CHANGELOG.md'),
    [string] $StampVersion,
    [string] $Date
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# This script's output is read by a human cutting a release and, when it fails,
# is the reason the release stopped. ANSI colour codes in a redirected stream
# read as line noise, and PowerShell 7 emits them unless told otherwise.
$PSStyle.OutputRendering = 'PlainText'
# The refusal IS the output on the path that matters. PowerShell 7's default
# ConciseView wraps it in caret art pointing at this script's own source line,
# which buries the sentence a person is meant to act on.
$ErrorView = 'NormalView'

$Path = [System.IO.Path]::GetFullPath($Path)

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Error "There is no changelog at '$Path'. A release cannot describe itself without one, and the empty-section check has nothing to be empty of. Create it (Keep a Changelog format, '## [Unreleased]' at the top)."
    exit 1
}

$content = Get-Content -LiteralPath $Path -Raw

# The heading is matched exactly as the format writes it, brackets and all.
# A changelog that has lost its unreleased section is a different failure from
# one whose section is empty, and it gets a different message: the first is a
# file somebody edited by hand, the second is work nobody wrote down.
$section = [regex]::Match($content, '(?ms)^\#\#[ \t]+\[Unreleased\][ \t]*\r?$(.*?)(?=^\#\#[ \t]|\z)')

if (-not $section.Success) {
    Write-Error "'$Path' has no '## [Unreleased]' heading, so there is nothing to release from and nothing for the empty-section check to read. Restore the heading rather than working around it."
    exit 1
}

$notes = $section.Groups[1].Value.Trim()

# A list item, at the start of a line, allowing the indentation a nested item
# carries. Subheads, prose and blank lines are not entries.
$items = [regex]::Matches($notes, '(?m)^[ \t]*[-*][ \t]+\S')

if ($items.Count -eq 0) {
    Write-Error "'$Path' has no entries under '## [Unreleased]', so this release cannot say what changed. Write them from the work as it landed, not from the git log now: reconstruction at release time is the failure this check exists to catch."
    exit 1
}

if ($StampVersion) {
    # The same shape the build derives and the packager accepts: three parts,
    # optionally a pre-release suffix, never a fourth part and never a leading
    # `v`. `0.0.0` is refused here for the same reason the build refuses it --
    # it is what a derivation that found no tag produces.
    if ($StampVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        Write-Error "'$StampVersion' is not a version this project can cut. Three parts and an optional pre-release suffix, with no leading 'v' (the tag carries that) and no fourth part (vpk rejects four-part versions outright)."
        exit 1
    }

    if ($StampVersion -match '^0\.0\.0([-+]|$)') {
        Write-Error "'$StampVersion' means the version was derived from no git tag. A binary that does not know what it is cannot be rolled back to or bisected against, so there is nothing here worth stamping a changelog for."
        exit 1
    }

    if ($content -match ('(?m)^\#\#[ \t]+\[' + [regex]::Escape($StampVersion) + '\]')) {
        Write-Error "'$Path' already has a section for $StampVersion. Cutting the same version twice would leave two sections claiming the same tag."
        exit 1
    }

    $when = if ($Date) { $Date } else { (Get-Date).ToString('yyyy-MM-dd') }

    if ($when -notmatch '^\d{4}-\d{2}-\d{2}$') {
        Write-Error "'$when' is not an ISO 8601 date (yyyy-MM-dd)."
        exit 1
    }

    $heading = $section.Value.Split("`n")[0].TrimEnd("`r")
    $stamped = $content.Remove($section.Index, $heading.Length).Insert(
        $section.Index,
        ($heading + [Environment]::NewLine + [Environment]::NewLine + '## [' + $StampVersion + '] - ' + $when))

    Set-Content -LiteralPath $Path -Value $stamped -NoNewline -Encoding utf8NoBOM
    Write-Verbose "Stamped $StampVersion into $Path."
}

# Last, and to stdout: this is what goes in the release notes.
$notes
