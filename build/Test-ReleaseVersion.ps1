# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    The release-validation rule: MONOTONIC, OR AN EXPLICIT ROLLBACK REPUBLISH.

.DESCRIPTION
    Build-order step 19, and the pipeline half of rollback. It is a separate
    script from New-Release.ps1 for one reason: THE SUITE HAS TO BE ABLE TO
    DRIVE IT. A rule that only exists inside a release script is a rule nobody
    exercises until the day it matters, and this one has two halves that must
    agree with a setting on the other side of the wire.

    BOTH HALVES OR NEITHER WORKS:

      * The CLIENT half is `AllowVersionDowngrade = true`, in
        src/BrowserAI/Updates/VelopackUpdateClient.cs. Its default is false, and
        with it off a rollback is reported to the user as "no updates" -- an
        older version in the feed is simply not seen.

      * The PIPELINE half is this. A rule of "strictly increasing" alone makes a
        rollback impossible to PUBLISH while the runtime happily accepts one,
        which is exactly the state ExoFabric/UCC is in today: the client would
        take a rollback and the version-validation script refuses to emit one.

    So a lower version is permitted, and permitted only as a STATED INTENT.
    -RollbackRepublish is what states it. Without the switch a lower version is
    an accident -- a stale checkout, a detached HEAD, a hand-typed number -- and
    publishing it would silently downgrade every client on the channel.

.PARAMETER Manifest
    The channel's `releases.<channel>.json`. A path that does not exist means an
    empty channel, and any version is acceptable on one.

.PARAMETER Version
    The version being cut.

.PARAMETER RollbackRepublish
    States that this release is deliberately older than the newest published.

.OUTPUTS
    The decision, as a string: `first`, `monotonic` or `rollback`. A refusal is
    a non-zero exit and a message.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Manifest,
    [Parameter(Mandatory)] [string] $Version,
    [switch] $RollbackRepublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSStyle.OutputRendering = 'PlainText'
$ErrorView = 'NormalView'

# The same shape the build derives and the packager accepts. `vpk` rejects
# four-part versions outright, so a fourth part is refused here rather than
# discovered at pack time.
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    Write-Error "'$Version' is not a version this project can cut. Three parts and an optional pre-release suffix, with no leading 'v' (the tag carries that) and no fourth part (vpk rejects four-part versions outright)."
    exit 1
}

if ($Version -match '^0\.0\.0([-+]|$)') {
    Write-Error "'$Version' means the version was derived from no git tag. A binary that does not know what it is cannot be rolled back to or bisected against."
    exit 1
}

if (-not (Test-Path -LiteralPath $Manifest)) {
    # An empty channel is not an error. It is also, from the client's point of
    # view, indistinguishable from a misconfigured feed URL: both answer 404.
    'first'
    exit 0
}

$feed = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json

$published = @()
if ($feed.PSObject.Properties.Name -contains 'Assets') {
    $published = @($feed.Assets | Where-Object { $_.Type -eq 'Full' } | ForEach-Object { $_.Version })
}

if ($published.Count -eq 0) {
    'first'
    exit 0
}

$highest = ($published |
    ForEach-Object { [System.Management.Automation.SemanticVersion]::Parse($_) } |
    Sort-Object -Descending |
    Select-Object -First 1)

$candidate = [System.Management.Automation.SemanticVersion]::Parse($Version)

if ($candidate -eq $highest) {
    Write-Error "$Version is already the newest release on this channel. Republishing a version over itself would leave two packages claiming one version, and a client cannot tell them apart -- the feed is keyed on the version, not on the bytes."
    exit 1
}

if ($candidate -lt $highest) {
    if (-not $RollbackRepublish) {
        Write-Error "$Version is older than the published $highest. That is a ROLLBACK, and it is permitted -- the client sets AllowVersionDowngrade, so it WILL be accepted and every machine on this channel will move backwards -- but only as a stated intent. Re-run with -RollbackRepublish if that is what you mean."
        exit 1
    }

    'rollback'
    exit 0
}

'monotonic'
exit 0
