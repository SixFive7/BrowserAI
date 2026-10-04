# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Puts RegisterAI.exe into the payload, checked against its release's own
    SHA256SUMS, and records what it put there.

.DESCRIPTION
    Q349, decided 2026-10-01 by the maintainer, verbatim: "Q349 a". BrowserAI
    registers itself with Claude Code and Codex by running RegisterAI, a program
    of its own repository and release cycle, and the build takes it the way it
    takes node: the newest release, floating, and refused unless its bytes match
    the checksum list published beside it.

    By default the newest release of SixFive7/RegisterAI is read from GitHub's
    REST API and its two assets, RegisterAI.exe and SHA256SUMS, downloaded from
    the release, with no sign-in and no gh: the repository is public since
    2026-10-04, Q379, decided by the maintainer, verbatim: "Q379 b". -From names a
    folder holding the same two files instead: a release downloaded by hand for
    an offline build, or RegisterAI's own artifacts\release to try a build before
    it is released. The check is the same either way, so the override changes
    where the files come from and never whether they are checked.
    Corrected 2026-10-04 (previously "By default the newest release of
    SixFive7/RegisterAI is read and its two assets, RegisterAI.exe and
    SHA256SUMS, downloaded with gh. While that repository is private gh must be
    signed in to an account that can read it. -From names a folder holding the
    same two files instead: a downloaded release, or RegisterAI's own
    artifacts\release.").

    In order:
      1. SHA256SUMS must carry a line for RegisterAI.exe, and the file must hash to
         it. Otherwise nothing is written.
      2. RegisterAI.exe --version must exit 0 and print a version; taken from a
         release, it must be the release's own tag.
      3. The file is copied to <PayloadRoot>\registerai\RegisterAI.exe, and the
         copy is hashed again.
      4. payload.json, when it exists, gains a registerai block, and the committed
         provenance stamp build/payload/registerai.json is rewritten, the way the
         npm lock is copied back to build/payload/package-lock.json.

.PARAMETER PayloadRoot
    The payload being assembled. Gitignored; never committed.

.PARAMETER From
    A folder holding RegisterAI.exe and SHA256SUMS, used in place of the newest
    release: for an offline build, or to try a RegisterAI build before it is
    released.

.PARAMETER Repository
    The GitHub repository the release is read from.

.PARAMETER StampPath
    The committed provenance stamp.

.EXAMPLE
    pwsh -File build/Get-RegisterAi.ps1
.EXAMPLE
    pwsh -File build/Get-RegisterAi.ps1 -From C:\src\RegisterAI\artifacts\release
#>
[CmdletBinding()]
param(
    [string] $PayloadRoot = (Join-Path $PSScriptRoot '..' 'payload'),
    [string] $From = '',
    [string] $Repository = 'SixFive7/RegisterAI',
    [string] $StampPath = (Join-Path $PSScriptRoot 'payload' 'registerai.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$PSNativeCommandUseErrorActionPreference = $false

$fileName = 'RegisterAI.exe'
$sumsName = 'SHA256SUMS'
$PayloadRoot = [System.IO.Path]::GetFullPath($PayloadRoot)
$StampPath = [System.IO.Path]::GetFullPath($StampPath)

# ---------------------------------------------------------------------------
# 1. Where the two files come from.
# ---------------------------------------------------------------------------

if ($From) {
    $folder = [System.IO.Path]::GetFullPath($From)
    $tag = $null
    $release = $null
    $source = 'folder'
    Write-Host "RegisterAI from the folder $folder"
}
else {
    # The repository is public (Q379 b), so the read and both downloads go out
    # with no sign-in. GitHub answers 60 unauthenticated API calls an hour from
    # one address, and a build makes one.
    $api = "https://api.github.com/repos/$Repository/releases/latest"

    try {
        $latest = Invoke-RestMethod -Uri $api -Headers @{ Accept = 'application/vnd.github+json' }
    }
    catch {
        throw "Reading $api failed: $($_.Exception.Message) The read needs the network and no sign-in, and GitHub allows 60 unauthenticated API calls an hour from one address. Or pass -From with a folder holding $fileName and $sumsName."
    }

    $tag = $latest.tag_name
    $release = $latest.html_url
    $source = 'release'

    if (-not $tag -or -not $release) {
        throw "$api answered without a tag_name and an html_url, so there is no release to take. Nothing was put in the payload."
    }

    $folder = Join-Path $PayloadRoot '.cache' "registerai-$tag"
    New-Item -ItemType Directory -Force -Path $folder | Out-Null

    foreach ($name in @($fileName, $sumsName)) {
        $asset = @($latest.assets | Where-Object { $_.name -ceq $name })
        if ($asset.Count -ne 1) {
            throw "Release $tag of $Repository carries $($asset.Count) assets named $name, and the build takes exactly one. Nothing was put in the payload."
        }

        Invoke-WebRequest -Uri $asset[0].browser_download_url -OutFile (Join-Path $folder $name)
    }

    Write-Host "RegisterAI $tag from $release"
}

$exe = Join-Path $folder $fileName
$sums = Join-Path $folder $sumsName

foreach ($required in @($exe, $sums)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "'$required' is not there. A RegisterAI release carries $fileName and $sumsName, and both are needed."
    }
}

# ---------------------------------------------------------------------------
# 2. The bytes, against the release's own list.
# ---------------------------------------------------------------------------

$expected = Get-Content -LiteralPath $sums |
    Where-Object { $_ -match "^([0-9a-fA-F]{64})\s+\*?$([regex]::Escape($fileName))\s*$" } |
    ForEach-Object { $Matches[1].ToLowerInvariant() } |
    Select-Object -First 1

if (-not $expected) {
    throw "$sums carries no line for $fileName, so there is nothing to check it against. Nothing was put in the payload."
}

$actual = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) {
    throw "$exe hashed $actual and $sumsName says $expected. Nothing was put in the payload."
}

Write-Host "sha256: $actual (matches $sumsName)"

# ---------------------------------------------------------------------------
# 3. What it says it is.
# ---------------------------------------------------------------------------

$printed = (& $exe --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $printed -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "$exe --version exited $LASTEXITCODE and printed '$printed', which is not a version."
}

if ($tag -and "v$printed" -ne $tag) {
    throw "$exe says it is $printed and the release is tagged $tag."
}

Write-Host "version: $printed"

# ---------------------------------------------------------------------------
# 4. Into the payload, and on the record.
# ---------------------------------------------------------------------------

$targetDir = Join-Path $PayloadRoot 'registerai'
$target = Join-Path $targetDir $fileName

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
Copy-Item -LiteralPath $exe -Destination $target -Force

if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    Remove-Item -LiteralPath $target -Force
    throw "The copy at $target does not hash to $expected, so it was deleted."
}

$record = [ordered]@{
    version    = $printed
    tag        = $tag
    source     = $source
    repository = $Repository
    release    = $release
    sha256     = $expected
    bytes      = (Get-Item -LiteralPath $target).Length
}

$manifestPath = Join-Path $PayloadRoot 'payload.json'
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
    $manifest['registerai'] = $record
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
}

$stamp = [ordered]@{
    '_what_this_is' = 'What the last payload build put at payload\registerai\RegisterAI.exe, written by build/Get-RegisterAi.ps1. A provenance stamp like package-lock.json beside it, not a target: nothing reads it back to pin anything.'
    '_license'      = 'LicenseRef-BrowserAI-FSL-1.1-MIT-5yr, Copyright 2026 Jori Huisman'
}

foreach ($key in $record.Keys) {
    $stamp[$key] = $record[$key]
}

$stamp | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $StampPath -Encoding utf8NoBOM

Write-Host "RegisterAI.exe: $target"
Write-Host "Stamp: $StampPath"
