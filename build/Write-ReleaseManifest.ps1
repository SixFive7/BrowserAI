# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Writes the resolved-set manifest beside a release's archived package.

.DESCRIPTION
    plan/pre-release.md item 11: an artifact that cannot state exactly what went
    into it is not releasable, because that is what makes a rollback meaningful
    and a regression bisectable.

    NOTHING EMITTED THIS UNTIL NOW. The first run of the checklist, 2026-08-16,
    satisfied item 11 by copying six files BY HAND into .work/step20/manifest/ --
    and a hand-assembled manifest is one nobody assembles twice, which is why
    this script exists rather than a paragraph of instructions.

    Six files, copied rather than transcribed, plus a manifest.json stating the
    version, the tag it came from, the package and its SHA-256, and the resolved
    version each copied file states. Copied is the operative word: a transcribed
    version number is a number somebody typed, and the whole point of the
    manifest is that it is not.

    IT REFUSES ON A MISSING FILE, NAMING IT. A manifest with five of six files
    in it looks exactly like a complete one to whoever reads it a year later, so
    a partial manifest is worse than none. In particular payload/payload.json
    only exists once build/Build-Payload.ps1 has run, and a release cut without
    a payload is not a release.

    THIS SCRIPT IS SEPARATE FROM New-Release.ps1 SO THE SUITE CAN DRIVE IT, the
    same reason Test-ReleaseVersion.ps1 is separate. A rule that only exists
    inside a release script is one nobody exercises until the day it matters.

.PARAMETER Root
    The repository root the six files are read from. Defaults to this script's
    parent, which is what a release does.

.PARAMETER Destination
    Where the manifest directory is written. The release script points this
    beside the archived .nupkg.

.PARAMETER Version
    The derived version this release was packed at.

.PARAMETER Tag
    The output of `git describe --tags --long`. Read from git when omitted, and
    recorded as null when there is no git or no tag, never invented.

.PARAMETER Channel
    The Velopack channel the release was packed on.

.PARAMETER Package
    The full .nupkg. Its size and SHA-256 are recorded so the manifest names one
    exact artifact rather than a version.

.EXAMPLE
    pwsh -File build/Write-ReleaseManifest.ps1 -Destination Releases/archive/0.1.1-manifest -Version 0.1.1
#>
[CmdletBinding()]
param(
    [string] $Root,
    [Parameter(Mandatory = $true)][string] $Destination,
    [Parameter(Mandatory = $true)][string] $Version,
    [string] $Tag,
    [string] $Channel = 'win',
    [string] $Package
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSStyle.OutputRendering = 'PlainText'

if (-not $Root) { $Root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')) }

# The exact six of pre-release.md item 11, with the flattened names the first
# hand-assembled manifest used, so the two are comparable.
$wanted = [ordered]@{
    'src-BrowserAI.packages.lock.json'            = 'src/BrowserAI/packages.lock.json'
    'tests-BrowserAI.Tests.packages.lock.json'    = 'tests/BrowserAI.Tests/packages.lock.json'
    'tests-BrowserAI.TestProbe.packages.lock.json' = 'tests/BrowserAI.TestProbe/packages.lock.json'
    'payload.package-lock.json'                   = 'build/payload/package-lock.json'
    'payload.json'                                = 'payload/payload.json'
    'browsers.json'                               = 'upstream-snapshots/browsers.json'
}

$missing = @()
foreach ($entry in $wanted.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root $entry.Value))) { $missing += $entry.Value }
}

if ($missing) {
    Write-Error ("The resolved set cannot be recorded: " + ($missing -join ', ') + " is missing from $Root. A manifest holding five of six files reads exactly like a complete one to whoever opens it a year from now, so this refuses rather than writing a partial. If payload/payload.json is the missing one, run build/Build-Payload.ps1 first: a release cut without a payload is not a release.")
    exit 1
}

$null = New-Item -ItemType Directory -Force -Path $Destination
$destination = [System.IO.Path]::GetFullPath($Destination)

$files = [ordered]@{}
foreach ($entry in $wanted.GetEnumerator()) {
    $source = Join-Path $Root $entry.Value
    Copy-Item -LiteralPath $source -Destination (Join-Path $destination $entry.Key) -Force
    $files[$entry.Key] = [ordered]@{
        from  = $entry.Value
        bytes = (Get-Item -LiteralPath $source).Length
    }
}

# --- What each copied file states ---------------------------------------------
# Read back out of the COPIES, so the manifest states what it holds rather than
# what the repository held a moment ago.
function Get-LockVersion([string] $lockFile, [string] $package) {
    $lock = Get-Content -LiteralPath (Join-Path $destination $lockFile) -Raw | ConvertFrom-Json
    $found = $lock.dependencies.PSObject.Properties.Value | ForEach-Object {
        $_.PSObject.Properties | Where-Object Name -eq $package
    } | Select-Object -First 1

    if ($found) { $found.Value.resolved } else { $null }
}

# ⚠️ -AsHashtable IS MANDATORY FOR THE NPM LOCK, and only the real file says so.
# `packages` carries the root project under the EMPTY-STRING key, and
# ConvertFrom-Json refuses an empty property name without it: "The provided JSON
# includes a property whose name is an empty string". Measured 2026-08-16 on the
# first run of this script against a real release -- the synthetic lock the suite
# drives it with had no root entry, so the test passed where the release failed,
# and the test's fixture now carries one.
$npm = Get-Content -LiteralPath (Join-Path $destination 'payload.package-lock.json') -Raw | ConvertFrom-Json -AsHashtable
$payload = Get-Content -LiteralPath (Join-Path $destination 'payload.json') -Raw | ConvertFrom-Json
$browsers = Get-Content -LiteralPath (Join-Path $destination 'browsers.json') -Raw | ConvertFrom-Json

$npmVersion = {
    param($name)
    if ($npm.packages.ContainsKey("node_modules/$name")) { $npm.packages["node_modules/$name"].version } else { $null }
}

if (-not $Tag) {
    # Never invented: a tag that could not be read is recorded as null, because
    # a manifest naming a tag nobody can check is worse than one admitting it
    # does not know.
    $described = & git -C $Root describe --tags --long 2>$null
    if ($LASTEXITCODE -eq 0 -and $described) { $Tag = $described.Trim() }
}

$packageRecord = $null
if ($Package -and (Test-Path -LiteralPath $Package)) {
    $item = Get-Item -LiteralPath $Package
    $packageRecord = [ordered]@{
        name   = $item.Name
        bytes  = $item.Length
        sha256 = (Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    '_what_this_is' = 'The resolved set this release was cut from. pre-release.md item 11. Copied, never transcribed: every version below was read back out of the file beside it.'
    writtenUtc      = (Get-Date).ToUniversalTime().ToString('o')
    version         = $Version
    tag             = $Tag
    channel         = $Channel
    package         = $packageRecord
    files           = $files
    resolved        = [ordered]@{
        nuget    = [ordered]@{
            ModelContextProtocol = Get-LockVersion 'src-BrowserAI.packages.lock.json' 'ModelContextProtocol'
            Velopack             = Get-LockVersion 'src-BrowserAI.packages.lock.json' 'Velopack'
            MinVer               = Get-LockVersion 'src-BrowserAI.packages.lock.json' 'MinVer'
            TUnit                = Get-LockVersion 'tests-BrowserAI.Tests.packages.lock.json' 'TUnit'
        }
        npm      = [ordered]@{
            '@playwright/mcp' = & $npmVersion '@playwright/mcp'
            'playwright-core' = & $npmVersion 'playwright-core'
        }
        node     = [ordered]@{
            version = $payload.node.version
            lts     = $payload.node.lts
            sha256  = $payload.node.sha256
        }
        browsers = [ordered]@{}
    }
}

foreach ($browser in $browsers.browsers) {
    $manifest.resolved.browsers[$browser.name] = [ordered]@{
        revision       = $browser.revision
        browserVersion = if ($browser.PSObject.Properties.Name -contains 'browserVersion') { $browser.browserVersion } else { $null }
    }
}

$manifestFile = Join-Path $destination 'manifest.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestFile -Encoding utf8NoBOM

Write-Host "Resolved-set manifest: $destination ($($wanted.Count) files + manifest.json)"
$destination
