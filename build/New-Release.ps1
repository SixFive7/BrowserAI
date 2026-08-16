# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Publishes BrowserAI ahead-of-time, checks the two things only a publish
    wrapper can check, and packs a Velopack release.

.DESCRIPTION
    Build-order step 19. This is the release script `build/` did not have, and
    steps 1 and 18 both deferred work to it. It does seven things, in order,
    and refuses rather than warns at every one of them:

      1. `vpk` and the Velopack library must be the SAME version. The CLI writes
         the package format the library reads, and nothing else in this
         repository can enforce it: `vpk` is a global tool, so it is outside
         packages.lock.json entirely. A mismatch is a package the client cannot
         read, discovered on a user's machine.

      2. THE VERSION IS DERIVED, NEVER TYPED. MinVer reads the git tag; this
         script asks the build what it derived and refuses `0.0.0` and,
         unless -AllowPreRelease, anything carrying a suffix. A release cut
         from an untagged build is one that can never be rolled back to.

      3. RELEASE VALIDATION: MONOTONIC OR AN EXPLICIT ROLLBACK REPUBLISH.
         This is the pipeline half of rollback, and it only works paired with
         `AllowVersionDowngrade` on the client (VelopackUpdateClient sets it).
         Turn on one without the other and the runtime accepts a rollback the
         build refuses to emit, which is the state ExoFabric/UCC is in today.
         A republish of an older version is permitted only with
         -RollbackRepublish, so it is a stated intent rather than an accident.

      4. ILC'S RAW OUTPUT MUST BE EMPTY, and only reading it can establish that.
         `SuppressTrimAnalysisWarnings=false` + `ILLinkTreatWarningsAsErrors`
         already fail the publish on any IL2xxx/IL3xxx WARNING. They do not
         cover the case the requirement was written for: ILC reports an
         always-throwing method as neither a warning nor an error, and a
         publish that emitted `Method '...' will always throw because: Failed
         to load assembly '...'` exited 0 with zero warnings and produced an
         artifact. No MSBuild property catches that. This does.
         (TODO.md, "Capture ILC's raw output and fail the publish if it is
         non-empty".)

      5. NO DECORATED VERSION STRING ANYWHERE IN THE LINKED BINARY. SixFive7/
         FrameLink shipped `0.0.0+a273b31` followed by a 40-character sha; its
         updater MATCHES the served version against the reported one, so the
         two could never be equal, and every frame in the fleet downloaded the
         binary it was already running, swapped it, restarted, and repeated
         hourly, forever. BrowserAI's updater matches too. The repository-wide
         property and BuildVersionTests cover the entry assembly's own
         attribute; this covers every string a REFERENCED project contributed
         to the same AOT binary, which nothing else would see.
         (TODO.md, "Check the *published* binary's version string".)

      6. `vpk pack`, per-user, never --msi. --msi PerMachine installs to
         Program Files and makes the updater self-elevate; a UAC prompt cannot
         be answered by a background MCP server.

      7. ARCHIVE THE FULL .nupkg. Velopack prunes `packages\` to the current
         full package and deltas are forward-only, so an unarchived release is
         one that can only be rolled back to by a fresh full download.

    What it deliberately does NOT do: publish, push, tag, or decide that a
    release happens. plan/pre-release.md item 14 is a human.

.PARAMETER Channel
    The Velopack channel. Lower-case: `vpk pack` lower-cases what it writes
    into the manifest name while the client does not, so a mixed-case channel
    resolves on NTFS and 404s on a case-sensitive object store.

.PARAMETER OutputDir
    Where vpk writes the release. Defaults to `Releases/` at the repository
    root, which .gitignore already covers.

.PARAMETER ArchiveDir
    Where full packages are kept forever. Defaults to `Releases/archive`.

.PARAMETER PackDir
    The publish output to pack. Defaults to a fresh AOT publish this script
    performs itself.

.PARAMETER RollbackRepublish
    States that this release is deliberately older than one already in the
    feed. Without it, a non-monotonic version is refused.

.PARAMETER AllowPreRelease
    Permits packing a version carrying a pre-release suffix. For exercising
    the update lane, never for a release.

.PARAMETER SkipPublish
    Use the existing -PackDir instead of publishing. The ILC and version-string
    checks are skipped with it, and say so.

.EXAMPLE
    pwsh -File build/New-Release.ps1
#>
[CmdletBinding()]
param(
    [string] $Channel = 'win',
    [string] $OutputDir,
    [string] $ArchiveDir,
    [string] $PackDir,
    [switch] $RollbackRepublish,
    [switch] $AllowPreRelease,
    [switch] $SkipPublish,
    [string] $PackVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSStyle.OutputRendering = 'PlainText'
$ErrorView = 'NormalView'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'src' 'BrowserAI' 'BrowserAI.csproj'
if (-not $OutputDir) { $OutputDir = Join-Path $root 'Releases' }
if (-not $ArchiveDir) { $ArchiveDir = Join-Path $OutputDir 'archive' }

$packId = 'BrowserAI'

# --- 1. vpk and Velopack must agree ------------------------------------------
# The tool is global, so it is outside packages.lock.json and nothing else in
# the repository can see it. Read the resolved library version out of the lock
# file rather than out of Directory.Packages.props, which says `*`.
$lock = Get-Content -LiteralPath (Join-Path $root 'src' 'BrowserAI' 'packages.lock.json') -Raw | ConvertFrom-Json
$velopack = $lock.dependencies.PSObject.Properties.Value | ForEach-Object {
    $_.PSObject.Properties | Where-Object Name -eq 'Velopack'
} | Select-Object -First 1

if (-not $velopack) {
    Write-Error "The Velopack package is not in src/BrowserAI/packages.lock.json, so there is nothing to check the vpk tool against. Restore first."
    exit 1
}

$libraryVersion = $velopack.Value.resolved

# vpk has no --version flag: it answers "Unrecognized command or argument".
# The version is in the first line of its own help banner ("Velopack CLI 1.2.0,
# for distributing applications."), which is the only place it states it.
$banner = (& vpk --help --legacyConsole 2>&1 | Out-String)
$toolVersion = ([regex]::Match($banner, 'Velopack CLI\s+(?<v>\d+\.\d+\.\d+[0-9A-Za-z.\-]*)')).Groups['v'].Value

if (-not $toolVersion) {
    Write-Error "Could not read the vpk tool version from its own help banner. Is it installed? 'dotnet tool install -g vpk --version $libraryVersion'."
    exit 1
}

if ($toolVersion -ne $libraryVersion) {
    Write-Error "The vpk tool is $toolVersion and the Velopack library resolved to $libraryVersion. The CLI writes the package format the library reads, so a mismatch produces a package the client cannot read, and it is discovered on a user's machine rather than here. Run: dotnet tool update -g vpk --version $libraryVersion"
    exit 1
}

Write-Host "vpk $toolVersion matches Velopack $libraryVersion."

# --- 2. The version is derived ------------------------------------------------
if (-not $PackVersion) {
    $derived = & dotnet msbuild $project -t:MinVer -getProperty:MinVerVersion -v:quiet 2>&1 | Out-String
    $PackVersion = ([regex]::Match($derived, '(?m)^\s*(?<v>\d+\.\d+\.\d+[0-9A-Za-z.\-+]*)\s*$')).Groups['v'].Value
}

if (-not $PackVersion) {
    Write-Error "Could not derive a version. MinVer answers this from the nearest git tag; a checkout with no tags produces nothing here and a shallow clone produces 0.0.0. 'git fetch --tags', or set fetch-depth to 0."
    exit 1
}

if ($PackVersion -match '^0\.0\.0([-+]|$)') {
    Write-Error "The build derived $PackVersion, which means MinVer found no 'v*' tag. A binary that does not know what it is cannot be rolled back to or bisected against."
    exit 1
}

if ($PackVersion -match '\+') {
    Write-Error "The derived version $PackVersion carries build metadata. The update path MATCHES the served version against the reported one, and a decorated copy can never equal an undecorated one -- this is the FrameLink hourly-restart failure. IncludeSourceRevisionInInformationalVersion must stay false."
    exit 1
}

if (($PackVersion -match '-') -and -not $AllowPreRelease) {
    Write-Error "The derived version $PackVersion carries a pre-release suffix, which means HEAD is not on a tag. Never self-update from a build that is not a release. Tag it, or pass -AllowPreRelease to exercise the update lane."
    exit 1
}

Write-Host "Packing version $PackVersion on channel $Channel."

# --- 3. Monotonic, or an explicit rollback republish ---------------------------
# The rule lives in its own script so THE SUITE CAN DRIVE IT. A rule that only
# exists inside a release script is one nobody exercises until the day it
# matters, and this one has to agree with a setting on the other side of the
# wire (AllowVersionDowngrade, in VelopackUpdateClient). One implementation, two
# callers: here, and ReleaseScriptTests.
$feedManifest = Join-Path $OutputDir "releases.$Channel.json"
$decision = & (Join-Path $PSScriptRoot 'Test-ReleaseVersion.ps1') `
    -Manifest $feedManifest -Version $PackVersion -RollbackRepublish:$RollbackRepublish

if ($LASTEXITCODE -ne 0) { exit 1 }

if ($decision -eq 'rollback') {
    Write-Warning "ROLLBACK REPUBLISH: $PackVersion is older than what is published on channel '$Channel', and -RollbackRepublish was given."
} else {
    Write-Host "Release validation: $decision."
}

# --- 4/5. Publish, read ILC's raw output, and scan the linked binary -----------
if (-not $PackDir) {
    $PackDir = Join-Path $root 'artifacts' 'publish-release'
}

if (-not $SkipPublish) {
    if (Test-Path -LiteralPath $PackDir) { Remove-Item -LiteralPath $PackDir -Recurse -Force }

    $ilcLog = Join-Path $root '.work' 'release-publish.log'
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ilcLog)

    Write-Host "Publishing (NativeAOT) to $PackDir ..."

    # ⚠️ THE BINARY AND THE PACKAGE MUST CARRY THE SAME VERSION. When the caller
    # names one, the publish is told the same number, so the manifest `vpk`
    # stamps and the attribute MinVer stamps cannot disagree. A build packed at
    # one version and compiled at another is exactly the state that made a fleet
    # download the binary it was already running, hourly, forever.
    $publishArgs = @($project, '-c', 'Release', '-r', 'win-x64', '--self-contained', '-o', $PackDir, '-v:normal')
    if ($PSBoundParameters.ContainsKey('PackVersion')) {
        $publishArgs += "-p:MinVerVersionOverride=$PackVersion"
    }

    # -v:normal, because ILC's own console output is what is being read and a
    # quieter verbosity drops it. Redirected to a file rather than streamed:
    # a grandchild that inherits the pipe keeps it open after the command has
    # exited, and the declared timeout then never fires.
    & dotnet publish @publishArgs *>&1 | Tee-Object -FilePath $ilcLog | Out-Null
    $publishExit = $LASTEXITCODE

    $ilc = Get-Content -LiteralPath $ilcLog

    if ($publishExit -ne 0) {
        Write-Error "The publish failed with exit code $publishExit. Its output is in $ilcLog."
        exit 1
    }

    # THE CHECK NO MSBUILD PROPERTY CAN MAKE. An always-throwing method is not a
    # diagnostic, so it has no code, no severity, and nothing to treat as an
    # error -- it is a line of console text and nothing else.
    # ⚠️ THE DIAGNOSTIC CODE ALONE IS NOT A MATCH, and getting that wrong is a
    # check that never goes green. At -v:normal the log contains csc's full
    # command line, which carries `/nowarn:...,IL2121,...` -- so a bare
    # `\bIL[0-9]{4}\b` matches a SUPPRESSION LIST and fails every publish.
    # Measured 2026-08-16 on the first run of this script. The severity word is
    # what makes it a diagnostic rather than an argument.
    $ilcComplaints = $ilc | Where-Object {
        $_ -match 'will always throw' -or
        $_ -match '(?i)\b(warning|error)\s+IL[0-9]{4}\b' -or
        $_ -match '\bAOT analysis warning\b' -or
        $_ -match '\bTrim analysis warning\b'
    }

    if ($ilcComplaints) {
        Write-Error ("ILC's output is not empty, and a publish that emits any of these can still exit 0 with an artifact:`n" +
            ($ilcComplaints -join "`n") + "`nFull output: $ilcLog")
        exit 1
    }

    Write-Host "ILC output is clean ($($ilc.Count) lines read, 0 complaints)."

    # FrameLink's guard, and it is stronger than an assertion on the entry
    # assembly's own attribute: a referenced project carrying a decorated
    # string is linked into this same binary and nothing else would say so.
    $binary = Join-Path $PackDir 'BrowserAI.exe'

    if (-not (Test-Path -LiteralPath $binary)) {
        Write-Error "The publish produced no $binary."
        exit 1
    }

    $bytes = [System.IO.File]::ReadAllBytes($binary)
    $text = [System.Text.Encoding]::Unicode.GetString($bytes) + "`n" + [System.Text.Encoding]::ASCII.GetString($bytes)

    # `<version core>+<sha>` is the shape the SDK produces, in both the `+` and
    # the `.`-separated forms.
    $decorated = [regex]::Matches($text, '\d+\.\d+\.\d+(?:-[0-9A-Za-z.\-]+)?\+[0-9a-f]{7,40}') |
        ForEach-Object { $_.Value } | Sort-Object -Unique

    # ⚠️ CORRECTED 2026-08-16, ON THE FIRST RUN (previously: fail on ANY
    # decorated string anywhere in the binary, which is how TODO.md described
    # FrameLink's build.sh). THAT CHECK CAN NEVER GO GREEN HERE. Measured: the
    # first publish of this repository carried SIX decorated strings and not one
    # of them was ours -- Velopack 1.2.0+f2edcbc, ModelContextProtocol
    # 2.2.0+6fa3825, Microsoft.Extensions.* 10.0.10 / 10.0.11 / 10.8.3, each
    # decorated by its own publisher's SourceLink and linked into this binary by
    # ILC. FrameLink's sweep is only sound for a build with no third-party
    # dependencies carrying one, which is not this build and will not become one.
    #
    # WHAT ACTUALLY MATTERS IS NARROWER AND IS STILL A SWEEP: a decorated string
    # whose version CORE is the version being packed. That is ours -- the entry
    # assembly's attribute, or a referenced project of ours sharing the derived
    # version -- and it is the only string that can reach the feed comparison,
    # because the updater matches BuildVersion.Current against the served
    # version. A third-party package's own decoration is inert.
    $ours = $decorated | Where-Object { $_ -like "$PackVersion+*" -or $_ -like "$PackVersion.*" }

    if ($ours) {
        Write-Error ("The linked binary reports THIS build's version in decorated form:`n" +
            ($ours -join "`n") + "`nThe updater MATCHES the served version against the reported one, so a decorated copy can never equal it -- this is FrameLink's hourly restart loop. Find the project that set SourceRevisionId; IncludeSourceRevisionInInformationalVersion is false repository-wide in Directory.Build.props.")
        exit 1
    }

    Write-Host "No decorated version string for $PackVersion in the linked binary ($($decorated.Count) third-party decorations present and inert)."
} else {
    Write-Warning "-SkipPublish: the ILC output check and the decorated-version-string scan did NOT run for this pack."
}

if (-not (Test-Path -LiteralPath (Join-Path $PackDir 'BrowserAI.exe'))) {
    Write-Error "There is no BrowserAI.exe in $PackDir, so there is nothing to pack."
    exit 1
}

# --- 6. Pack -------------------------------------------------------------------
$null = New-Item -ItemType Directory -Force -Path $OutputDir

$packArgs = @(
    'pack'
    '--packId', $packId
    '--packVersion', $PackVersion
    '--packDir', $PackDir
    '--packTitle', 'BrowserAI'
    '--packAuthors', 'Jori Huisman'
    '--channel', $Channel
    '--outputDir', $OutputDir
    # ⚠️ NEVER the execution stub. The stub is compiled
    # `#![windows_subsystem = "windows"]` and returns in 59 ms while the app
    # runs on, so an MCP client registered against it sees its server die
    # instantly. Registration names <root>\current\BrowserAI.exe directly;
    # --mainExe is what makes that path exist and be the one Update.exe starts.
    '--mainExe', 'BrowserAI.exe'
    # No Desktop or Start Menu entry: this is a background stdio server that a
    # human never launches. The default is Desktop,StartMenuRoot.
    '--shortcuts', 'None'
    # ⚠️ --msi is NOT passed, ever. --msi PerMachine installs to Program Files
    # and makes the updater self-elevate, and a UAC prompt cannot be answered by
    # a background MCP server. Per-user to %LocalAppData% is the whole design.
)

Write-Host "vpk $($packArgs -join ' ')"
& vpk @packArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "vpk pack failed with exit code $LASTEXITCODE."
    exit 1
}

# --- 7. Archive the full package ----------------------------------------------
$null = New-Item -ItemType Directory -Force -Path $ArchiveDir
$full = Join-Path $OutputDir "$packId-$PackVersion-full.nupkg"
$delta = Join-Path $OutputDir "$packId-$PackVersion-delta.nupkg"

if (-not (Test-Path -LiteralPath $full)) {
    Write-Error "vpk did not produce $full, so there is nothing to archive and no rollback target for this release."
    exit 1
}

Copy-Item -LiteralPath $full -Destination $ArchiveDir -Force

$fullSize = (Get-Item -LiteralPath $full).Length
$allFiles = Get-ChildItem -LiteralPath $PackDir -Recurse -File
$packDirSize = ($allFiles | Measure-Object -Property Length -Sum).Sum

# ⚠️ THE RATIO IS AGAINST WHAT SHIPS, NOT AGAINST WHAT IS ON DISK. `vpk pack`
# defaults to --exclude .*\.pdb, and this build's pdb is 76 MB against a 168 MB
# publish directory -- so a ratio taken over the raw directory understates the
# compression by more than a factor of two and is not a number anyone should
# quote. Measured 2026-08-16, when the first pack reported 0.3502 for what is
# really 0.51.
$shipped = ($allFiles | Where-Object { $_.Extension -ne '.pdb' } | Measure-Object -Property Length -Sum).Sum
$deltaSize = if (Test-Path -LiteralPath $delta) { (Get-Item -LiteralPath $delta).Length } else { $null }

[pscustomobject]@{
    Version          = $PackVersion
    Channel          = $Channel
    PackDirBytes     = $packDirSize
    ShippedBytes     = $shipped
    FullPackageBytes = $fullSize
    DeltaPackageBytes = $deltaSize
    CompressionRatio = [math]::Round($fullSize / $shipped, 4)
    FullPackage      = $full
    DeltaPackage     = if ($deltaSize) { $delta } else { $null }
    Archived         = (Join-Path $ArchiveDir (Split-Path -Leaf $full))
    Setup            = (Join-Path $OutputDir "$packId-$Channel-Setup.exe")
    Manifest         = (Join-Path $OutputDir "releases.$Channel.json")
}
