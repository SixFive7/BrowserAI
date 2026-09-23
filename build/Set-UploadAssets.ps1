# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Rewrites `assets.<channel>.json` to the declared upload set, and refuses if
    what lands on disk is not it.

.DESCRIPTION
    ⚠️ THE PACKER'S LIST AND THE UPLOAD SET ARE TWO DIFFERENT ANSWERS TO ONE
    QUESTION, AND UNTIL 2026-09-23 THEY DISAGREED. `vpk pack` writes
    `assets.<channel>.json` naming everything it produced -- installer, portable
    archive and full package -- and `vpk upload github` then uploads EVERY file
    listed in it (`BuildAssets.Read` -> `build.GetFilePaths()`, read at Velopack
    1.2.158). So the portable archive would have been published by the one
    command nobody runs today, contradicting the set `New-Release.ps1` declares
    and RELEASING states. Nothing would have said so: the two mechanisms never
    meet.

    Q235 b, the maintainer's answer. This step makes the packer's own list agree
    with the declaration, and refuses rather than warns.

    ⚠️ WHAT IT CANNOT COVER, SAID HERE RATHER THAN LEFT TO BE DISCOVERED.
    `vpk upload github` also uploads two files that are NOT in this list and
    cannot be removed from it: `releases.<channel>.json`, which it generates from
    the `Full` entries that survive here, and -- on the default Windows channel --
    a legacy `RELEASES`, unconditionally. The first is wanted and is why a `Full`
    entry surviving is a refusal condition below. The second is not, and this
    file cannot stop it; what stops it is that this project publishes with
    `gh release create` from the declared set and not with `vpk upload`.

    In its own script, and not inline in `New-Release.ps1`, for the reason
    `Clear-TestPackFeed.ps1` and `Test-ReleaseVersion.ps1` are: so the suite can
    DRIVE it. A rewrite asserted by reading the release script for a line proves
    the line was typed, never that anything was rewritten.

.PARAMETER Path
    The `assets.<channel>.json` to rewrite.

.PARAMETER Keep
    The declared upload set, as file names. Names in it that the packer never
    produced -- `releases.<channel>.json` is one, because `vpk` generates that
    itself -- are simply absent from the file and are not an error.

.EXAMPLE
    pwsh -File build/Set-UploadAssets.ps1 -Path Releases/assets.win.json -Keep BrowserAI.exe,BrowserAI.app-1.1.0-full.nupkg,releases.win.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Path,
    [Parameter(Mandatory)] [string[]] $Keep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSStyle.OutputRendering = 'PlainText'
$ErrorView = 'NormalView'

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Error "There is no asset list at $Path, so nothing can say which files an upload would publish."
    exit 1
}

try {
    $entries = @(Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
}
catch {
    Write-Error "$Path is not readable as JSON, so the upload set cannot be enforced against it: $($_.Exception.Message)"
    exit 1
}

foreach ($entry in $entries) {
    if (-not ($entry.PSObject.Properties.Name -contains 'RelativeFileName')) {
        Write-Error "$Path holds an entry with no RelativeFileName, so what an upload would publish cannot be read from it."
        exit 1
    }
}

# ⚠️ A COMMA-SEPARATED `-Keep` ARRIVES AS ONE ELEMENT UNDER `pwsh -File`,
# AND SO DOES NOTHING ELSE -- measured 2026-09-23. `-File` passes arguments as
# literal strings: `-Keep a,b,c` binds a single `"a,b,c"` to a `[string[]]`, and
# `-Keep a b c` binds `a` and treats the rest as positional. So a caller that
# cannot pass a real array -- which is every caller outside PowerShell, the suite
# included -- has exactly one spelling available, and this splits it. Called from
# `New-Release.ps1` with a real array, the split is a no-op.
$names = @($Keep | ForEach-Object { $_ -split ',' } | Where-Object { $_.Length -gt 0 })

$kept = @($entries | Where-Object { $names -contains $_.RelativeFileName })
$dropped = @($entries | Where-Object { $names -notcontains $_.RelativeFileName })

# ⚠️ A LIST WITH NO `Full` ENTRY IS A FEED WITH NO ROWS. `vpk upload` builds
# `releases.<channel>.json` out of the Full and Delta entries that survive here,
# so dropping the package would publish a manifest advertising nothing -- which
# is the shape a client reports as "no update available" and never as an error.
if (-not @($kept | Where-Object { $_.Type -eq 'Full' })) {
    Write-Error "Rewriting $Path to the declared upload set would leave no entry of type Full, and the release manifest is generated from those, so the result would be a feed advertising nothing."
    exit 1
}

# LF and no BOM, like every other file this repository writes. `-Compress` keeps
# the shape `vpk` itself writes rather than reformatting a file it reads back.
$json = ($kept | Select-Object RelativeFileName, Type | ConvertTo-Json -Compress -AsArray)
[System.IO.File]::WriteAllText($Path, ($json -replace "`r`n", "`n"), (New-Object System.Text.UTF8Encoding $false))

# ⚠️ RE-READ FROM DISK RATHER THAN TRUSTING THE VARIABLE. What an upload reads is
# the file, so what is asserted is the file.
$written = @(Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
$strangers = @($written | Where-Object { $names -notcontains $_.RelativeFileName } | ForEach-Object { $_.RelativeFileName })

if ($strangers.Count -gt 0) {
    Write-Error "$Path still names $($strangers -join ', '), which is not in the declared upload set, so an upload from it would publish something this release does not."
    exit 1
}

if ($dropped.Count -gt 0) {
    Write-Host "Dropped from $(Split-Path -Leaf $Path): $(($dropped | ForEach-Object { $_.RelativeFileName }) -join ', ')."
}

Write-Host "$(Split-Path -Leaf $Path) names $($written.Count) file(s), all of them in the declared upload set: $(($written | ForEach-Object { $_.RelativeFileName }) -join ', ')."
