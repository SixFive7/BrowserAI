# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Builds the BrowserAI runtime payload, and provisions one Chromium for the
    test rig.

.DESCRIPTION
    Build-order step 3. Two halves that are deliberately not the same thing:

    1. THE PAYLOAD, which ships. `node.exe` for the newest Node LTS, plus the
       `@playwright/mcp` tree resolved from the npm `latest` dist-tag. Both are
       re-resolved from nothing on every run -- `node_modules` and
       `package-lock.json` are deleted first -- so a stale lock can never hold a
       version back. The lock that comes out is copied back to
       `build/payload/package-lock.json` and committed as the provenance stamp.

    2. THE TEST RIG'S BROWSER, which does not ship. BrowserAI provisions
       browsers on first run (plan/A-runtime.md, "First-run browser
       provisioning"); this is upstream's own installer, run once, so that every
       step after this one has a real browser to test against. BrowserAI's own
       provisioning subsystem -- the non-blocking `init`, the timers, the error
       text, the reinstall tool -- is build-order step 15 and is not this.

    Nothing here reads a version from a file. `latest` is resolved by npm and
    the Node LTS by nodejs.org/dist/index.json, per CLAUDE.md, "Versioning:
    everything floats, the build freezes it".

.PARAMETER PayloadRoot
    Where the payload is assembled. Gitignored; never committed.

.PARAMETER BrowsersPath
    The browsers root for the test rig. MUST be absolute: a relative value
    resolves against INIT_CWD -- inherited from any npm ancestor -- before cwd
    (kb/playwright/provisioning-and-timings.md).

.PARAMETER SeedBrowsersFrom
    An existing Playwright browsers root to hard-copy matching revisions from
    before running the installer, so a machine that already holds the exact
    revision does not re-download it. Only directories whose revision matches
    the resolved `browsers.json` are copied, and the installer still runs and
    still decides. Empty by default: the honest default is to let the installer
    download.

.PARAMETER SkipBrowser
    Build the payload only. For a packaging run, which needs no browser.

.EXAMPLE
    pwsh -File build/Build-Payload.ps1 -SeedBrowsersFrom "$env:LOCALAPPDATA\ms-playwright"
#>
[CmdletBinding()]
param(
    [string] $PayloadRoot = (Join-Path $PSScriptRoot '..' 'payload'),
    [string] $BrowsersPath = (Join-Path $env:LOCALAPPDATA 'BrowserAI' 'browsers'),
    [string] $SeedBrowsersFrom = '',
    [switch] $SkipBrowser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# Exit codes are checked by name below, so the failure says which step failed
# rather than which executable did.
$PSNativeCommandUseErrorActionPreference = $false

function Assert-ExitCode {
    param([Parameter(Mandatory)][string] $What)

    if ($LASTEXITCODE -ne 0) {
        throw "$What exited $LASTEXITCODE."
    }
}

function Write-Step {
    param([Parameter(Mandatory)][string] $Message)

    Write-Host ''
    Write-Host "==> $Message"
}

function Get-TreeSize {
    param([Parameter(Mandatory)][string] $Path)

    $measured = Get-ChildItem -LiteralPath $Path -Recurse -File -Force |
        Measure-Object -Property Length -Sum
    return [int64]($measured.Sum ?? 0)
}

$PayloadRoot = [System.IO.Path]::GetFullPath($PayloadRoot)
$BrowsersPath = [System.IO.Path]::GetFullPath($BrowsersPath)
$sourceDir = Join-Path $PSScriptRoot 'payload'
$mcpDir = Join-Path $PayloadRoot 'mcp'
$nodeDir = Join-Path $PayloadRoot 'node'
$cacheDir = Join-Path $PayloadRoot '.cache'
$startedUtc = [datetime]::UtcNow

Write-Host "BrowserAI payload build"
Write-Host "  payload root : $PayloadRoot"
Write-Host "  browsers root: $BrowsersPath"

# ---------------------------------------------------------------------------
# 1. The JS tree. Resolved from empty, every time.
# ---------------------------------------------------------------------------

Write-Step 'Resolving @playwright/mcp from the npm `latest` dist-tag'

$npm = (Get-Command 'npm.cmd' -CommandType Application).Source
Write-Host "npm: $npm"

New-Item -ItemType Directory -Force -Path $mcpDir | Out-Null
Remove-Item -LiteralPath (Join-Path $mcpDir 'node_modules') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $mcpDir 'package-lock.json') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $sourceDir 'package.json') -Destination $mcpDir -Force

# Contain anything upstream might add a postinstall for. PLAYWRIGHT_BROWSERS_PATH
# is absolute for the reason in the parameter help; the skip flag means an
# install script cannot quietly pull ~200 MB during `npm install`.
#
# Both stay set for the rest of the script, including the explicit
# install-browser call below, and that is deliberate. Measured 2026-08-16
# @ playwright-core 1.63.0-alpha-2026-08-05: PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD
# gates only `installBrowsersForNpmInstall` and `ensureConfiguredBrowserInstalled`
# -- with it set, `install-browser` against an empty root still downloaded. So
# leaving it set costs nothing today, and if upstream ever extends it to the
# explicit path this script fails loudly on the chrome.exe assertion instead of
# producing a payload with no browser behind it.
$env:PLAYWRIGHT_BROWSERS_PATH = $BrowsersPath
$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = '1'

Push-Location $mcpDir
try {
    & $npm install --omit=dev --no-audit --no-fund
    Assert-ExitCode 'npm install'
}
finally {
    Pop-Location
}

$lockPath = Join-Path $mcpDir 'package-lock.json'
$resolvedLock = Get-Content -LiteralPath $lockPath -Raw
# -AsHashtable is required, not stylistic: npm keys the root package of a lock
# on the empty string, and ConvertFrom-Json refuses that in object mode.
$lock = $resolvedLock | ConvertFrom-Json -AsHashtable

$packages = $lock['packages']
if (-not $packages.ContainsKey('node_modules/@playwright/mcp') -or -not $packages.ContainsKey('node_modules/playwright-core')) {
    throw 'The resolved lock does not contain both @playwright/mcp and playwright-core.'
}

$mcpEntry = $packages['node_modules/@playwright/mcp']
$mcpVersion = $mcpEntry['version']
$coreVersion = $packages['node_modules/playwright-core']['version']
$declaredCore = if ($mcpEntry.ContainsKey('dependencies')) { $mcpEntry['dependencies']['playwright-core'] } else { $null }

# playwright-core is never resolved independently. If upstream ever loosened
# that pin to a range, the payload would start floating on a second axis and
# nothing else would say so.
if ($declaredCore -ne $coreVersion) {
    throw "@playwright/mcp $mcpVersion declares playwright-core '$declaredCore' but the tree resolved $coreVersion. Upstream's pin is no longer exact; see UPSTREAM-REVIEW.md before proceeding."
}

# An install script in the shipped tree runs on the build machine with the
# build machine's environment. Today there are none outside optional,
# platform-excluded packages; the day there is one, that is a review item and
# not something to discover from a payload that is already assembled.
$scripted = @($packages.GetEnumerator() |
    Where-Object {
        $entry = $_.Value
        $entry.ContainsKey('hasInstallScript') -and
        $entry['hasInstallScript'] -and
        -not ($entry.ContainsKey('optional') -and $entry['optional'])
    } |
    ForEach-Object { $_.Key })

if ($scripted.Count -gt 0) {
    throw "The resolved tree declares install scripts in: $($scripted -join ', '). Adjudicate that in UPSTREAM-REVIEW.md before vendoring it."
}

Write-Host "@playwright/mcp: $mcpVersion"
Write-Host "playwright-core: $coreVersion (@playwright/mcp's own exact dependency, not npm latest)"

Write-Step 'Verifying the lock reproduces the tree on its own'

Push-Location $mcpDir
try {
    & $npm ci --omit=dev --no-audit --no-fund
    Assert-ExitCode 'npm ci'
}
finally {
    Pop-Location
}

if ((Get-Content -LiteralPath $lockPath -Raw) -ne $resolvedLock) {
    throw 'npm ci rewrote package-lock.json, so the lock did not describe the tree it produced.'
}

Copy-Item -LiteralPath $lockPath -Destination (Join-Path $sourceDir 'package-lock.json') -Force

# `.links/` holds absolute paths of the machine that installed a browser.
# Measured 2026-08-16: playwright-core only ever writes it into the BROWSERS
# root, never into node_modules -- `path.join(registryDirectory, '.links')` in
# playwright-core/lib/coreBundle.js. Asserted anyway, because the requirement is
# about what the payload contains and a future upstream could move it.
$strays = @(Get-ChildItem -LiteralPath $PayloadRoot -Recurse -Force -Directory -Filter '.links' -ErrorAction SilentlyContinue)
foreach ($stray in $strays) {
    Write-Host "Stripping $($stray.FullName)"
    Remove-Item -LiteralPath $stray.FullName -Recurse -Force
}

# ---------------------------------------------------------------------------
# 2. node.exe, and Node's full LICENSE.
# ---------------------------------------------------------------------------

Write-Step 'Resolving the newest Node LTS'

$index = Invoke-RestMethod -Uri 'https://nodejs.org/dist/index.json'
$release = $index |
    Where-Object { $_.PSObject.Properties['lts'] -and $_.lts } |
    Select-Object -First 1

if ($null -eq $release) {
    throw 'nodejs.org/dist/index.json returned no entry carrying an lts field.'
}

$nodeVersion = $release.version
Write-Host "node: $nodeVersion ($($release.lts), released $($release.date))"

# The zip rather than dist/<version>/win-x64/node.exe: measured 2026-08-16, the
# bare node.exe has no LICENSE beside it, and plan/A-runtime.md requires Node's
# full LICENSE to ship because it aggregates the OpenSSL, ICU, V8, zlib and
# c-ares terms. The archive is also ~57 MB smaller than the raw binary.
$archiveName = "node-$nodeVersion-win-x64.zip"
$archivePath = Join-Path $cacheDir $archiveName
New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null

$sums = Invoke-RestMethod -Uri "https://nodejs.org/dist/$nodeVersion/SHASUMS256.txt"
$expected = ($sums -split "`n" |
    Where-Object { $_ -match "^([0-9a-f]{64})\s+$([regex]::Escape($archiveName))\s*$" } |
    ForEach-Object { $Matches[1] } |
    Select-Object -First 1)

if (-not $expected) {
    throw "SHASUMS256.txt for $nodeVersion carries no entry for $archiveName."
}

$haveCached = (Test-Path -LiteralPath $archivePath) -and
    ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ieq $expected)

if ($haveCached) {
    Write-Host "Using cached $archiveName"
}
else {
    Write-Host "Downloading $archiveName"
    Invoke-WebRequest -Uri "https://nodejs.org/dist/$nodeVersion/$archiveName" -OutFile $archivePath
    $actual = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actual -ine $expected) {
        Remove-Item -LiteralPath $archivePath -Force
        throw "$archiveName hashed $actual, expected $expected. Deleted."
    }
}

Write-Host "sha256: $($expected.ToLowerInvariant())"

Remove-Item -LiteralPath $nodeDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $nodeDir | Out-Null

$archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($wanted in @('node.exe', 'LICENSE')) {
        $entryName = "node-$nodeVersion-win-x64/$wanted"
        $entry = $archive.Entries | Where-Object { $_.FullName -ceq $entryName } | Select-Object -First 1
        if ($null -eq $entry) {
            throw "$archiveName contains no entry '$entryName'."
        }

        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $nodeDir $wanted), $true)
    }
}
finally {
    $archive.Dispose()
}

$nodeExe = Join-Path $nodeDir 'node.exe'
$reported = (& $nodeExe --version)
Assert-ExitCode 'node --version'

if ($reported -ne $nodeVersion) {
    throw "The extracted node.exe reports $reported, but the resolver returned $nodeVersion."
}

Write-Host "node.exe --version: $reported"

# ---------------------------------------------------------------------------
# 3. The test rig's browser. Not the product's provisioning subsystem.
# ---------------------------------------------------------------------------

$browsersJson = Join-Path $mcpDir 'node_modules' 'playwright-core' 'browsers.json'
$browsers = (Get-Content -LiteralPath $browsersJson -Raw | ConvertFrom-Json).browsers
$chromium = $browsers | Where-Object { $_.name -ceq 'chromium' } | Select-Object -First 1

# The outer directory uses underscores where the browser name uses dashes, and
# the inner one uses dashes. A path built consistently is wrong; this is the
# only place that conversion is written down in code.
function Get-BrowserDirectoryName {
    param([Parameter(Mandatory)][pscustomobject] $Browser)

    return "$($Browser.name -replace '-', '_')-$($Browser.revision)"
}

if ($SkipBrowser) {
    Write-Step 'Skipping the browser install (-SkipBrowser)'
}
else {
    Write-Step "Provisioning chromium $($chromium.revision) ($($chromium.browserVersion)) for the test rig"

    New-Item -ItemType Directory -Force -Path $BrowsersPath | Out-Null

    if ($SeedBrowsersFrom) {
        $seedRoot = [System.IO.Path]::GetFullPath($SeedBrowsersFrom)
        foreach ($browser in $browsers | Where-Object { $_.name -cin @('chromium', 'ffmpeg', 'winldd') }) {
            $name = Get-BrowserDirectoryName -Browser $browser
            $from = Join-Path $seedRoot $name
            $to = Join-Path $BrowsersPath $name

            if ((Test-Path -LiteralPath $from) -and -not (Test-Path -LiteralPath $to)) {
                Write-Host "Seeding $name from $seedRoot"
                Copy-Item -LiteralPath $from -Destination $to -Recurse -Force
            }
        }
    }

    # --no-shell is load-bearing, not tidiness: chrome-headless-shell is never
    # provisioned (README, settled 2026-08-15 -- full Chromium in every mode).
    & $nodeExe (Join-Path $mcpDir 'node_modules' '@playwright' 'mcp' 'cli.js') install-browser chromium --no-shell --no-progress
    Assert-ExitCode 'install-browser chromium'

    $chromeExe = Join-Path $BrowsersPath (Get-BrowserDirectoryName -Browser $chromium) 'chrome-win64' 'chrome.exe'
    if (-not (Test-Path -LiteralPath $chromeExe)) {
        throw "The installer reported success but $chromeExe does not exist."
    }

    $shell = $browsers | Where-Object { $_.name -ceq 'chromium-headless-shell' } | Select-Object -First 1
    $shellDir = Join-Path $BrowsersPath (Get-BrowserDirectoryName -Browser $shell)
    if (Test-Path -LiteralPath $shellDir) {
        throw "$shellDir exists. --no-shell did not hold, and every mode-selection assumption downstream rests on it."
    }

    Write-Host "chrome.exe: $chromeExe"
}

# ---------------------------------------------------------------------------
# 4. What was built.
# ---------------------------------------------------------------------------

Write-Step 'Payload'

$manifest = [ordered]@{
    '_what_this_is'  = 'What the last payload build resolved. A build artifact, not a target: nothing reads it back to pin anything.'
    builtUtc         = $startedUtc.ToString('o')
    node             = [ordered]@{
        version = $nodeVersion
        lts     = $release.lts
        date    = $release.date
        archive = $archiveName
        sha256  = $expected.ToLowerInvariant()
        bytes   = (Get-Item -LiteralPath $nodeExe).Length
    }
    npm              = [ordered]@{
        '@playwright/mcp' = $mcpVersion
        'playwright-core' = $coreVersion
        bytes             = Get-TreeSize -Path $mcpDir
    }
    browsers         = [ordered]@{
        root      = $BrowsersPath
        installed = -not $SkipBrowser.IsPresent
        chromium  = [ordered]@{
            revision       = $chromium.revision
            browserVersion = $chromium.browserVersion
            directory      = Get-BrowserDirectoryName -Browser $chromium
        }
    }
}

$manifestPath = Join-Path $PayloadRoot 'payload.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

$manifest | ConvertTo-Json -Depth 6 | Write-Host
Write-Host ''
Write-Host "Manifest: $manifestPath"
Write-Host "Lock: $(Join-Path $sourceDir 'package-lock.json')"
Write-Host "Elapsed: $([int]([datetime]::UtcNow - $startedUtc).TotalSeconds)s"
