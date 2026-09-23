# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Publishes BrowserAI ahead-of-time, checks the two things only a publish
    wrapper can check, and packs a Velopack release.

.DESCRIPTION
    Build-order step 19. This is the release script `build/` did not have, and
    steps 1 and 18 both deferred work to it. It does the following, in order,
    and refuses instead of warning at every one of them.

    (This sentence read "It does seven things" until 2026-09-23 and had been
    wrong since the eighth item landed. It is not replaced with a new number:
    a hand-maintained count of the numbered list directly below it is the same
    defect again, one number later.)

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
         build refuses to emit, which is the state a shipping product this
         project studied is in today.
         A republish of an older version is permitted only with
         -RollbackRepublish, so it is a stated intent and not an accident.

      4. ILC'S RAW OUTPUT MUST BE EMPTY, and only reading it can establish that.
         `SuppressTrimAnalysisWarnings=false` + `TreatWarningsAsErrors`
         already fail the publish on any IL2xxx/IL3xxx WARNING.

         Corrected 2026-08-17 (previously `SuppressTrimAnalysisWarnings=false`
         + `ILLinkTreatWarningsAsErrors`). That second property has no
         observable effect: measured on SDK 10.0.400 / ILC 10.0.11 across five
         variants, setting it alone emitted 14 ILC warnings and exited 0 with a
         working binary, identical to setting nothing. The table is in
         src/BrowserAI/BrowserAI.csproj. Nothing about this script changes --
         the gate below was always the real cover for the case named next --
         but the sentence named a property that was not doing the work, and
         the next person to trim a "redundant" property would have cleared the
         wrong one.

         Neither property covers the case the requirement was written for:
         ILC reports an
         always-throwing method as neither a warning nor an error, and a
         publish that emitted `Method '...' will always throw because: Failed
         to load assembly '...'` exited 0 with zero warnings and produced an
         artifact. No MSBuild property catches that. This does.
         (TODO.md, "Capture ILC's raw output and fail the publish if it is
         non-empty".)

      5. NO DECORATED VERSION STRING ANYWHERE IN THE LINKED BINARY. A shipped
         product emitted `0.0.0+<sha>` followed by a 40-character sha; its
         updater MATCHES the served version against the reported one, so the
         two could never be equal, and every client in the fleet downloaded the
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

      8. RECORD THE RESOLVED SET BESIDE THE ARCHIVE. An artifact that cannot
         state exactly what went into it is not releasable: that is what makes
         a rollback meaningful and a regression bisectable. The first run of
         the checklist satisfied this item by copying six files BY HAND, and a
         hand-assembled manifest is one nobody assembles twice.
         (TODO.md, "Emit the resolved-set manifest from build/New-Release.ps1".)

      9. DECLARE THE UPLOAD SET. Which files a release publishes was, until
         2026-09-23, whatever whoever ran `gh release create` picked out of
         `Releases/` -- which is how one release's asset list became the next
         one's by matching and not by deciding. The set is declared in one
         place near the end of this script, refuses on a file that is not
         there, is printed as a ready-to-run `gh release create` line, and comes
         back as `Upload` on the returned object.
         (RELEASING.md, "What a release publishes".)

    What it deliberately does NOT do: publish, push, tag, or decide that a
    release happens. RELEASING.md item 14 is a human.

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

# ⚠️ TWO PROJECTS PUBLISH INTO ONE PACK DIRECTORY -- 2026-09-15. BrowserAI ships
# as two binaries: the configuration app, which is the Velopack main exe and
# what a person launches, and the MCP server, which is what a client starts. The
# app is FIRST because it is the smaller of the two and its ILC pass is the one
# most likely to be broken by a change to the interop layer; a release that is
# going to fail should fail on the cheap half.
#
# Each entry names the project, the file it produces and whether it carries the
# payload -- the last of those only so that a reader can see why the sizes
# differ by two orders of magnitude.
$project = Join-Path $root 'src' 'BrowserAI' 'BrowserAI.csproj'
$appProject = Join-Path $root 'src' 'BrowserAI.App' 'BrowserAI.App.csproj'
$publishes = @(
    @{ Project = $appProject; Exe = 'BrowserAI.exe';        What = 'configuration app' }
    @{ Project = $project;    Exe = 'BrowserAI.Server.exe'; What = 'MCP server' }
)

# The icon every artifact carries: the Setup stub, the Add/Remove entry, the
# Start Menu shortcut and both executables.
#
# ⚠️ IT IS CANDIDATE 3 OF THE TEN DRAWN ON 2026-09-15 -- a globe with a reading
# eye, chosen by the maintainer on 2026-09-16 (Q196). Corrected 2026-09-16
# (previously "IT IS A PLACEHOLDER UNTIL THE MAINTAINER CHOOSES ... candidate 1
# is copied in so that the packaging is complete and exercised"). It was one
# file then and it is one file now; RELEASING.md carries the pre-cut check, and
# ReleaseScriptTests.TheShippedIconIsTheOneTheMaintainerChose holds its shape.
$icon = Join-Path $root 'assets' 'BrowserAI.ico'
if (-not $OutputDir) { $OutputDir = Join-Path $root 'Releases' }
if (-not $ArchiveDir) { $ArchiveDir = Join-Path $OutputDir 'archive' }

# ⚠️ THE PACK ID IS THE INSTALL DIRECTORY -- 2026-09-15. Velopack derives the
# default install location from it and nothing else: `%LocalAppData%\<packId>`,
# immovable at 1.2.0 (there is no flag that changes it, and an id cannot carry a
# path). So the id is what puts the install root at
# `%LocalAppData%\BrowserAI.app`, beside the data root at
# `%LocalAppData%\BrowserAI` and not on top of it -- which is the whole
# preservation decision, because Setup.exe renames a non-empty install root
# aside and DELETES it, and uninstall empties it.
$packId = 'BrowserAI.app'

# What a human downloads is called BrowserAI and nothing else. `vpk` names its
# output after the pack id, so the installer arrives as
# `BrowserAI.app-win-Setup.exe` and the portable archive as
# `BrowserAI.app-win-Portable.zip`, and both are renamed below. The feed-internal
# `.nupkg` names are NOT renamed: Velopack resolves them by id out of
# `releases.<channel>.json`, and a rename there is a feed that 404s on the first
# update.
$downloadId = 'BrowserAI'

# ⚠️ THE SUITE INSTALLS THIS ONE, AND NEVER THE ONE ABOVE -- 2026-09-15.
# Velopack writes exactly one Add/Remove Programs key per pack id per user,
# named for the id and never for the location (registry.rs, read at 1.2.0): an
# install under `--installto` still rewrites
# `HKCU\...\Uninstall\<packId>` to point at the scratch root, and
# `Update.exe uninstall` from that root calls `delete_subkey_all(<id>)`
# UNCONDITIONALLY, with no comparison against InstallLocation. So an installer
# arm packed under the real id DESTROYS a real install's Add/Remove entry -- and
# a run killed part-way leaves it gone with nothing to restore it. Measured on
# this machine: no `BrowserAI.app` key after six installer-arm runs.
#
# The id and the TITLE are the delta. The test pack is built from the same
# publish directory, at the same version, on the same channel, with the same
# `--mainExe` and the same `--shortcuts` -- `$testPackArgs` below is `$packArgs`
# with three elements replaced -- so what the arm exercises is the same code path
# under names that cannot collide with anybody's install. Nothing published ever
# carries it: it is packed into a directory of its own and the resolved-set
# manifest names the eight files it always named. *(Eight since 2026-09-18,
# previously seven.)*
#
# *Corrected 2026-09-16 (previously "The id is the ONLY delta ... with two
# elements replaced")* -- it was not the only one, and the half that was missing
# is the one the Start Menu reads. See $testPackTitle below.
$testPackId = 'BrowserAI.app.test'

# What the pack calls itself. Read out of here by the suite, and used below
# instead of typed into $packArgs, so that the two packs' titles cannot drift
# apart from the variables the suite compares.
$packTitle = 'BrowserAI'

# ⚠️ AND THE SUITE'S PACK IS CALLED SOMETHING ELSE -- 2026-09-16. This is the
# SECOND name that has to differ, and it was missed when the id was split.
# Velopack names the Start Menu shortcut after the TITLE and never after the id
# (shortcuts.rs, read at 1.2.0: the link file is `<title>.lnk`), shortcut
# creation is NOT gated on `--silent` (install.rs), and the uninstall removes
# shortcuts BY TARGET. So two packs under one title share one `.lnk`: the
# suite's installer arm rewrote
# `%APPDATA%\Microsoft\Windows\Start Menu\Programs\BrowserAI.lnk` to point at its
# scratch root, and its own uninstall then deleted it -- destroying a real
# install's Start Menu entry exactly the way the shared pack id destroyed the
# real Add/Remove entry. Same defect, same fix, one file later.
#
# `BrowserAI (suite)` and not `BrowserAI.app.test`: a title is what a human sees
# in a Start Menu, so if one of these ever does survive a run it should say what
# it is. It also appears in nothing else in the package, which is what lets
# RealInstallerTests licence it to differ without licensing every binary that
# carries the word BrowserAI.
$testPackTitle = 'BrowserAI (suite)'

# What the suite's installer is called. `test-installer` and not anything
# resembling `BrowserAI.exe`: these two files sit one directory below the ones a
# person downloads, and a name that could be mistaken for a release artifact is
# the whole risk of packing twice.
$testDownloadId = 'BrowserAI.test'

# ⚠️ THE DOWNLOAD NAMES DROP THE CHANNEL ON THE DEFAULT CHANNEL AND KEEP IT
# OTHERWISE -- 2026-09-15. `BrowserAI.exe` and `BrowserAI.zip` are what a person
# should see on a releases page; `-win-Setup` and `-win-Portable` are vpk's
# vocabulary and not anybody's. But two channels packed into one output
# directory would then collide and the second would silently overwrite the
# first, which is the one property the old names had and this must not lose. So
# a non-default channel keeps its name: `BrowserAI-beta.exe`.
#
# Velopack's Setup stub does not read its own filename -- verified against
# 1.2.0's own source, which locates the package by the bundle appended to the
# executable and never by the path it was launched from -- so renaming it is
# safe in a way renaming a `.nupkg` is not.
$defaultChannel = 'win'
$downloadSuffix = if ($Channel -eq $defaultChannel) { '' } else { "-$Channel" }

# --- 1. vpk and Velopack must agree ------------------------------------------
# The tool is global, so it is outside packages.lock.json and nothing else in
# the repository can see it. Read the resolved library version out of the lock
# file and not out of Directory.Packages.props, which says `*`.
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
    Write-Error "The derived version $PackVersion carries build metadata. The update path MATCHES the served version against the reported one, and a decorated copy can never equal an undecorated one -- this is the observed hourly-restart failure a shipped product suffered fleet-wide. IncludeSourceRevisionInInformationalVersion must stay false."
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

    # ⚠️ THE INTERMEDIATES TOO, AND CLEARING THE OUTPUT DIRECTORY IS NOT ENOUGH
    # -- 2026-09-15. `IlcCompile` is an MSBuild target with Inputs and Outputs,
    # and its output is `obj\<config>\<tfm>\<rid>\native\BrowserAI.obj`. A
    # publish whose managed assemblies have not moved therefore SKIPS it --
    # "Skipping target "IlcCompile" because all output files are up-to-date" --
    # and produces a perfectly good binary by relinking the object file from
    # last time. There is no property that disables that check; the object file
    # IS the check, so removing it is the lever.
    #
    # Why that matters here, and is not a performance question: HALT-A,
    # the ILC-output scan below, reads ILC's own console output. On a skipped
    # pass there is none, so the scan sweeps a log ILC never wrote and reports
    # clean -- a check that cannot fail. Measured 2026-09-15 on this machine at
    # -v:normal: 95 lines with the pass, 75 without, and `Generating native
    # code` present in the first and absent in the second.
    #
    # Globbed on the framework moniker and not spelled, because the moniker
    # moves with the SDK and a path that stopped matching would silently restore
    # the incremental pass this exists to prevent.
    # ⚠️ BOTH PROJECTS' INTERMEDIATES. A per-project sweep that only knew about
    # one of them would leave the other's IlcCompile skippable, and HALT-A would
    # then read a log for a compilation that did not happen -- for exactly one
    # of the two binaries, which is the version of this defect that is hardest
    # to notice.
    foreach ($publish in $publishes) {
        $obj = Join-Path (Split-Path -Parent $publish.Project) 'obj' 'Release'

        foreach ($native in Get-ChildItem -Path $obj -Filter 'native' -Recurse -Directory -ErrorAction SilentlyContinue) {
            Write-Host "Removing ILC intermediates at $($native.FullName) so the publish cannot reuse last run's native object."
            Remove-Item -LiteralPath $native.FullName -Recurse -Force
        }
    }

    $null = New-Item -ItemType Directory -Force -Path (Join-Path $root '.work')

    # ⚠️ HALT-A RUNS ONCE PER PUBLISH, AND THAT IS THE WHOLE REASON THIS IS A
    # LOOP -- 2026-09-15. Two binaries are linked into one release, each by its
    # own ILC pass, and a scan that only read one of the two logs would ship a
    # binary nobody had checked while reporting that ILC's output was clean.
    foreach ($publish in $publishes) {
        $ilcLog = Join-Path $root '.work' ("release-publish-" + [System.IO.Path]::GetFileNameWithoutExtension($publish.Exe) + ".log")

        # ⚠️ EACH PUBLISH GETS ITS OWN DIRECTORY AND IS COPIED IN AFTERWARDS,
        # AND THAT IS NOT TIDINESS -- MEASURED 2026-09-15. Publishing both
        # projects with `-o` pointed at one directory produced a directory
        # holding `BrowserAI.Server.exe` and NOT `BrowserAI.exe`: the second
        # publish removed the first's executable, while leaving its `.pdb`
        # behind. What caught it was the both-present check below, which is the
        # only reason this is a paragraph and not an installer that starts a
        # Start Menu entry pointing at nothing.
        #
        # Diagnosed no further than that on purpose. A publish that cleans its
        # output directory is entitled to; what is not defensible is two
        # publishes sharing one, and separating them removes the question instead
        # of answering it.
        $stage = Join-Path $root 'artifacts' ("publish-" + [System.IO.Path]::GetFileNameWithoutExtension($publish.Exe))

        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }

        Write-Host "Publishing the $($publish.What) (NativeAOT) to $stage ..."

        # ⚠️ THE BINARY AND THE PACKAGE MUST CARRY THE SAME VERSION. When the
        # caller names one, the publish is told the same number, so the manifest
        # `vpk` stamps and the attribute MinVer stamps cannot disagree. A build
        # packed at one version and compiled at another is exactly the state
        # that made a fleet download the binary it was already running, hourly,
        # forever.
        $publishArgs = @($publish.Project, '-c', 'Release', '-r', 'win-x64', '--self-contained', '-o', $stage, '-v:normal')
        if ($PSBoundParameters.ContainsKey('PackVersion')) {
            $publishArgs += "-p:MinVerVersionOverride=$PackVersion"
        }

        # -v:normal, because ILC's own console output is what is being read and
        # a quieter verbosity drops it. Redirected to a file and not
        # streamed: a grandchild that inherits the pipe keeps it open after the
        # command has exited, and the declared timeout then never fires.
        & dotnet publish @publishArgs *>&1 | Tee-Object -FilePath $ilcLog | Out-Null
        $publishExit = $LASTEXITCODE

        $ilc = Get-Content -LiteralPath $ilcLog

        if ($publishExit -ne 0) {
            Write-Error "The publish of the $($publish.What) failed with exit code $publishExit. Its output is in $ilcLog."
            exit 1
        }

        # ⚠️ AND ILC ACTUALLY RAN, WHICH IS THE PREMISE OF EVERY LINE BELOW. A
        # skipped `IlcCompile` leaves a log with nothing of ILC's in it, and the
        # complaint scan then reports clean about a compilation that did not
        # happen. Its own script so that the refusal can be watched against a
        # log that shows a skipped pass -- a positive control this script cannot
        # give itself.
        & (Join-Path $PSScriptRoot 'Test-IlcFullPass.ps1') -Log $ilcLog
        if ($LASTEXITCODE -ne 0) {
            Write-Error "The publish log for the $($publish.What) shows no full ILC pass, so the checks below would be reading a compilation that did not happen."
            exit 1
        }

        # THE CHECK NO MSBUILD PROPERTY CAN MAKE. An always-throwing method is
        # not a diagnostic, so it has no code, no severity, and nothing to treat
        # as an error -- it is a line of console text and nothing else.
        # ⚠️ THE DIAGNOSTIC CODE ALONE IS NOT A MATCH, and getting that wrong is
        # a check that never goes green. At -v:normal the log contains csc's
        # full command line, which carries `/nowarn:...,IL2121,...` -- so a bare
        # `\bIL[0-9]{4}\b` matches a SUPPRESSION LIST and fails every publish.
        # Measured 2026-08-16 on the first run of this script. The severity word
        # is what makes it a diagnostic and not an argument.
        $ilcComplaints = $ilc | Where-Object {
            $_ -match 'will always throw' -or
            $_ -match '(?i)\b(warning|error)\s+IL[0-9]{4}\b' -or
            $_ -match '\bAOT analysis warning\b' -or
            $_ -match '\bTrim analysis warning\b'
        }

        if ($ilcComplaints) {
            Write-Error ("ILC's output for the $($publish.What) is not empty, and a publish that emits any of these can still exit 0 with an artifact:`n" +
                ($ilcComplaints -join "`n") + "`nFull output: $ilcLog")
            exit 1
        }

        Write-Host "ILC output for the $($publish.What) is clean ($($ilc.Count) lines read, 0 complaints)."

        # Into the one directory `vpk` packs. Copied and not published here,
        # for the reason above.
        $null = New-Item -ItemType Directory -Force -Path $PackDir
        Copy-Item -Path (Join-Path $stage '*') -Destination $PackDir -Recurse -Force

        Write-Host "Copied the $($publish.What) into $PackDir."
    }

    # The guard borrowed from the product that hit this, and it is stronger than
    # an assertion on the entry assembly's own attribute: a referenced project
    # carrying a decorated string is linked into this same binary and nothing
    # else would say so. Both binaries, because both link the same shared
    # library and either of them could carry the string.
    $text = ''

    foreach ($publish in $publishes) {
        $binary = Join-Path $PackDir $publish.Exe

        if (-not (Test-Path -LiteralPath $binary)) {
            Write-Error "The publish produced no $binary."
            exit 1
        }

        $bytes = [System.IO.File]::ReadAllBytes($binary)
        $text += [System.Text.Encoding]::Unicode.GetString($bytes) + "`n" + [System.Text.Encoding]::ASCII.GetString($bytes) + "`n"
    }

    # `<version core>+<sha>` is the shape the SDK produces, in both the `+` and
    # the `.`-separated forms.
    $decorated = [regex]::Matches($text, '\d+\.\d+\.\d+(?:-[0-9A-Za-z.\-]+)?\+[0-9a-f]{7,40}') |
        ForEach-Object { $_.Value } | Sort-Object -Unique

    # ⚠️ CORRECTED 2026-08-16, ON THE FIRST RUN (previously: fail on ANY
    # decorated string anywhere in the binary, which is how TODO.md described
    # the build script this check was modelled on). THAT CHECK CAN NEVER GO
    # GREEN HERE. Measured: the first publish of this repository carried SIX
    # decorated strings and not one of them was ours -- Velopack 1.2.0+f2edcbc,
    # ModelContextProtocol 2.2.0+6fa3825, Microsoft.Extensions.* 10.0.10 /
    # 10.0.11 / 10.8.3, each decorated by its own publisher's SourceLink and
    # linked into this binary by ILC. That whole-binary sweep is only sound for
    # a build with no third-party dependencies carrying one, which is not this
    # build and will not become one.
    #
    # WHAT ACTUALLY MATTERS IS NARROWER AND IS STILL A SWEEP: a decorated string
    # whose version CORE is the version being packed. That is ours -- the entry
    # assembly's attribute, or a referenced project of ours sharing the derived
    # version -- and it is the only string that can reach the feed comparison,
    # because the updater matches BuildVersion.Current against the served
    # version. A third-party package's own decoration is inert.
    $ours = $decorated | Where-Object { $_ -like "$PackVersion+*" -or $_ -like "$PackVersion.*" }

    if ($ours) {
        Write-Error ("A linked binary reports THIS build's version in decorated form:`n" +
            ($ours -join "`n") + "`nThe updater MATCHES the served version against the reported one, so a decorated copy can never equal it -- this is the hourly restart loop a shipped product ran fleet-wide. Find the project that set SourceRevisionId; IncludeSourceRevisionInInformationalVersion is false repository-wide in Directory.Build.props.")
        exit 1
    }

    Write-Host "No decorated version string for $PackVersion in either linked binary ($($decorated.Count) third-party decorations present and inert)."
} else {
    Write-Warning "-SkipPublish: the ILC output check and the decorated-version-string scan did NOT run for this pack."
}

# ⚠️ BOTH, and by name. A pack directory holding the app without the server is
# an installer that puts a window on somebody's Start Menu and registers nothing
# a client can start -- and RegistrationTarget refuses precisely that layout at
# install time, so the failure would be a successful install that says it could
# not find its own server.
foreach ($publish in $publishes) {
    if (-not (Test-Path -LiteralPath (Join-Path $PackDir $publish.Exe))) {
        Write-Error "There is no $($publish.Exe) in $PackDir, so the $($publish.What) is missing and there is nothing releasable to pack."
        exit 1
    }
}

# --- 6. Pack -------------------------------------------------------------------
$null = New-Item -ItemType Directory -Force -Path $OutputDir

$packArgs = @(
    'pack'
    '--packId', $packId
    '--packVersion', $PackVersion
    '--packDir', $PackDir
    '--packTitle', $packTitle
    '--packAuthors', 'Jori Huisman'
    '--channel', $Channel
    '--outputDir', $OutputDir
    # The icon on the Setup stub, the Add/Remove entry and the shortcut. See
    # $icon above for why it is a placeholder today.
    '--icon', $icon
    # ⚠️ THE CONFIGURATION APP, AND NEVER THE SERVER -- 2026-09-15. This one
    # name decides five things at once: which binary Setup.exe starts after a
    # non-silent install, which one the root stub and `Update.exe start` launch,
    # which one all four hooks run on, what the root stub is called, and what
    # the shortcut points at. A console-subsystem binary in this slot is given a
    # console by the post-install start and puts a terminal window on the user's
    # screen; nothing suppresses that start, so the answer is a binary that can
    # never be given a console.
    #
    # ⚠️ NEVER the execution stub, which is a different file: the stub sits at
    # <root>\BrowserAI.exe, is compiled `#![windows_subsystem = "windows"]` and
    # returns in 59 ms. Registration names <root>\current\BrowserAI.Server.exe,
    # composed from the app's own directory and checked for the console
    # subsystem before it is written.
    '--mainExe', 'BrowserAI.exe'
    # ⚠️ A START MENU ENTRY, since 2026-09-15 (previously 'None', "this is a
    # background stdio server that a human never launches"). That sentence was
    # true of the only binary there was; it is false of the one this name now
    # points at. The entry is how a person reaches the configuration app again
    # after the install, and without it the app could only ever be seen once.
    # StartMenuRoot and not the default Desktop,StartMenuRoot: a desktop icon
    # for a thing somebody opens twice a year is clutter.
    '--shortcuts', 'StartMenuRoot'
    # ⚠️ FULL PACKAGES ONLY, FOREVER -- 2026-09-22, the maintainer's decision,
    # verbatim: "always produce full packages only. The sizes are so small, and
    # internet speeds nowadays are so fast that we don't want to exert any
    # effort in creating deltas. Full downloads are always just easier."
    #
    # `None` is vpk's own name for it and was resolved from the tool and not
    # from memory: `vpk pack --help` documents `--delta <MODE>` and does
    # not enumerate the modes, so handing it one it cannot parse makes it name
    # them -- "Cannot parse argument 'ZZZINVALID' for option '--delta' as
    # expected type 'Velopack.Packaging.Compression.DeltaMode'. Did you mean one
    # of the following? None". Read 2026-09-22 at vpk 1.2.158.
    #
    # ⚠️ WITHOUT THIS THE DEFAULT IS `BestSpeed` AND DELTAS COME BACK SILENTLY.
    # Until 1.1.0 no release carried a delta, and not because anybody chose it:
    # the clean re-pack empties `Releases/`, so vpk never had a previous package
    # to compare against. The first cut that left one there would have started
    # emitting deltas with nothing to say so.
    # `ReleaseScriptTests.EveryReleasePacksFullPackagesOnlyAndTheFeedCarriesNoDeltaRow`
    # holds both halves -- that this argument is passed, and that it means what
    # this comment says -- with the positive control that the same pack without
    # it does produce a delta.
    '--delta', 'None'
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

# --- 6b. The downloads are called BrowserAI.exe and BrowserAI.zip --------------
# ⚠️ ADDED 2026-09-15 WITH THE PACK-ID RENAME and WIDENED THE SAME DAY to the
# two HUMAN-FACING artifacts *(previously the installer alone, renamed
# `BrowserAI.app-win-Setup.exe` -> `BrowserAI-win-Setup.exe`)*. `vpk` names
# everything after the pack id; the id is what chooses the install directory, so
# it had to become `BrowserAI.app`, and the files a person downloads must not
# inherit a suffix that exists to answer a question about directories -- nor
# vpk's own `-Setup` and `-Portable` vocabulary, which says what the tool calls
# them and not what they are.
#
# Exactly two artifacts are renamed and the feed-internal ones are the control:
# the `.nupkg`s and `releases.<channel>.json` keep the id, because Velopack
# resolves those by name and a rename there is a feed that 404s on the first
# update.
$downloads = @(
    @{ Packed = "$packId-$Channel-Setup.exe";    Download = "$downloadId$downloadSuffix.exe"; What = 'installer'; Required = $true }
    @{ Packed = "$packId-$Channel-Portable.zip"; Download = "$downloadId$downloadSuffix.zip"; What = 'portable archive'; Required = $true }
)

$assets = Join-Path $OutputDir "assets.$Channel.json"
$assetText = if (Test-Path -LiteralPath $assets) { Get-Content -LiteralPath $assets -Raw } else { $null }
$rewritten = $assetText

foreach ($download in $downloads) {
    $packedPath = Join-Path $OutputDir $download.Packed
    $downloadPath = Join-Path $OutputDir $download.Download

    if (-not (Test-Path -LiteralPath $packedPath)) {
        if ($download.Required) {
            Write-Error "vpk did not produce $packedPath, so there is no $($download.What) to rename or to publish."
            exit 1
        }

        continue
    }

    Move-Item -LiteralPath $packedPath -Destination $downloadPath -Force
    Write-Host "Renamed the $($download.What) to $(Split-Path -Leaf $downloadPath)."

    # And the asset manifest goes with it, because it is read by machines: a file
    # name in there that nothing on disk answers to is a lie in a machine-readable
    # file, which is worse than an inconvenient name.
    if ($null -ne $rewritten) {
        $rewritten = $rewritten.Replace($download.Packed, $download.Download)
    }
}

$setup = Join-Path $OutputDir "$downloadId$downloadSuffix.exe"
$portable = Join-Path $OutputDir "$downloadId$downloadSuffix.zip"

if (($null -ne $rewritten) -and ($rewritten -ne $assetText)) {
    # LF and no BOM, like every other file this repository writes.
    [System.IO.File]::WriteAllText($assets, ($rewritten -replace "`r`n", "`n"), (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Rewrote the download names in $(Split-Path -Leaf $assets)."
}

# --- 6c. The suite's installer, same publish, test id --------------------------
# ⚠️ IT GOES IN A DIRECTORY OF ITS OWN and nothing that publishes ever looks
# there. `Releases/test-pack/` holds the whole second pack -- installer, portable
# archive, `.nupkg` and its own `releases.<channel>.json` -- so the glob a person
# or a workflow runs over `Releases/` for the artifacts to upload cannot pick one
# up, and neither can `ReleaseLayout` when it is pointed at the real feed.
#
# `$testPackArgs` is `$packArgs` with exactly three elements replaced: the id,
# the TITLE and the output directory. Built and not retyped, so a packing
# decision added above reaches both packs and the suite cannot end up exercising
# an installer built differently from the one that ships.
#
# ⚠️ THE TITLE IS THE THIRD ONE AND IT WAS NOT ALWAYS THERE. *Corrected
# 2026-09-16 (previously "with exactly two elements replaced: the id and the
# output directory")* -- that was true of the code and wrong about the machine:
# the id decides the Add/Remove key and the install directory, and the TITLE
# decides the Start Menu shortcut's file name. Two elements left the two packs
# sharing one `.lnk`, which the suite's own uninstall then deleted. See
# $testPackTitle above.
$testOutputDir = Join-Path $OutputDir 'test-pack'
$null = New-Item -ItemType Directory -Force -Path $testOutputDir

# --- 6c-i. Clear the second feed BEFORE packing into it ------------------------
# ⚠️ THIS IS THE STEP THAT STOPS A RELEASE BEING REFUSED BY ITS OWN SCRATCH.
# The directory above is a FEED, not a folder: it holds `.nupkg`s, a
# `releases.<channel>.json`, a `RELEASES` file and an `assets.<channel>.json`,
# and `vpk` reads all of that when it decides whether the version being packed
# is newer than what is already there. A gate runs far more often than a release
# is cut, and every gate pack writes a PRE-RELEASE into this feed -- so at the
# moment of a real cut it holds versions ABOVE the release, and vpk refuses:
#
#     There is a release in channel win which is equal or greater to the
#     current version 1.0.0
#
# And because the shipping pack is step 6 and this is step 6c, that refusal
# fires AFTER the release itself has already been built. The non-zero exit names
# the suite's installer while the release sits finished on disk, which reads as
# a broken release and not as a dirty scratch directory. It refused the
# 2026-09-16 cut in exactly that shape, and the checklist correction written
# that day asked a human to clear four file names by hand before every cut.
#
# Q200, decided 2026-09-17: the script clears its own regenerated,
# never-published output. In its own file so the suite can DRIVE it -- an
# inline `Remove-Item` could only ever be asserted by reading this file for a
# line, which proves the line was typed and not that anything is removed.
& (Join-Path $PSScriptRoot 'Clear-TestPackFeed.ps1') `
    -Directory $testOutputDir -PackId $testPackId -DownloadId $testDownloadId -Channel $Channel

if ($LASTEXITCODE -ne 0) {
    Write-Error "Clearing the suite's own test-pack feed failed with exit code $LASTEXITCODE, so vpk would be packing into a feed that may already hold a newer pre-release and would refuse this cut after the release itself had been built."
    exit 1
}

$testPackArgs = @()
for ($i = 0; $i -lt $packArgs.Count; $i++) {
    switch ($packArgs[$i]) {
        '--packId'    { $testPackArgs += $packArgs[$i]; $testPackArgs += $testPackId;    $i++; continue }
        '--packTitle' { $testPackArgs += $packArgs[$i]; $testPackArgs += $testPackTitle; $i++; continue }
        '--outputDir' { $testPackArgs += $packArgs[$i]; $testPackArgs += $testOutputDir; $i++; continue }
        default       { $testPackArgs += $packArgs[$i] }
    }
}

Write-Host "vpk $($testPackArgs -join ' ')"
& vpk @testPackArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "vpk pack failed for the test pack with exit code $LASTEXITCODE, so the suite has no installer it may run."
    exit 1
}

$testDownloads = @(
    @{ Packed = "$testPackId-$Channel-Setup.exe";    Download = "$testDownloadId-installer.exe" }
    @{ Packed = "$testPackId-$Channel-Portable.zip"; Download = "$testDownloadId-portable.zip" }
)

foreach ($download in $testDownloads) {
    $packedPath = Join-Path $testOutputDir $download.Packed
    $downloadPath = Join-Path $testOutputDir $download.Download

    if (-not (Test-Path -LiteralPath $packedPath)) {
        Write-Error "vpk did not produce $packedPath, so the suite has no installer it may run."
        exit 1
    }

    Move-Item -LiteralPath $packedPath -Destination $downloadPath -Force
}

Write-Host "Packed the suite's own installer as $testDownloadId-installer.exe under $testOutputDir (pack id $testPackId). It is NEVER published."

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

# --- 8. Record the resolved set beside the archive -----------------------------
# In its own script for the same reason Test-ReleaseVersion.ps1 is: so the suite
# can drive it. It refuses on a missing file and that refusal must stop the
# release -- an artifact that cannot state what went into it is not releasable,
# and a manifest holding five of six files reads exactly like a complete one.
# Named for the download and not for the pack id: this directory is read by
# a person looking for what went into a release, and `BrowserAI.app-1.0.0-manifest`
# reads as a directory belonging to the installer and not to the release.
$manifestDir = Join-Path $ArchiveDir "$downloadId-$PackVersion-manifest"
$manifest = & (Join-Path $PSScriptRoot 'Write-ReleaseManifest.ps1') `
    -Root $root -Destination $manifestDir -Version $PackVersion -Channel $Channel -Package $full

if ($LASTEXITCODE -ne 0) { exit 1 }

# --- 9. The GitHub release body, generated from the section being cut ----------
# ⚠️ GENERATED, NOT CUT -- 2026-09-15. The body used to be the stamped
# section itself, truncated at whichever heading boundary fell nearest GitHub's
# 125,000-character field: 110,225 characters of the middle of an argument, with
# a permalink line stuck on at the cut. It is a document produced by hand at
# publish time, so nothing could reproduce it and nothing could check it.
#
# It goes beside the manifest for the same reason the manifest exists: it is
# part of the account of what was released, and a body written somewhere nobody
# looks is the hand-made one wearing a script. New-ReleaseNotes.ps1 refuses a
# section it cannot read and says which SHAPE it produced -- folded, or headlines
# alone when the folded one does not fit -- and that sentence belongs in the
# release record and not only on the screen of whoever ran this.
#
# ⚠️ NOT FOR A PRE-RELEASE VERSION, and that is the branch this script is run
# down most often, not an edge case. Every gate that installs a real
# installer needs a pack, and a pack is made by running THIS script on whatever
# MinVer derives from a commit past the tag -- `1.0.1-alpha.0.19` today. No such
# version has a changelog section and none ever will, so demanding one would
# make the release script unusable for the thing it is used for most. Found by
# running it: the pack succeeded and this step exited 1 naming a section nobody
# had written.
$bodyFile = $null
$bodyShape = $null

if ($PackVersion -match '-') {
    Write-Host "No release body for ${PackVersion}: it carries a pre-release suffix, so there is no changelog section for it and this pack is not a release."
}
else {
    $bodyFile = Join-Path $manifestDir "$downloadId-$PackVersion-release-body.md"
    $bodyReport = & (Join-Path $PSScriptRoot 'New-ReleaseNotes.ps1') `
        -Version $PackVersion -Destination $bodyFile

    if ($LASTEXITCODE -ne 0) { exit 1 }

    $bodyShape = ($bodyReport | Select-Object -Last 1)
}

# --- 10. What a release PUBLISHES, declared once and read, not judged -----------
# ⚠️ THIS LIST IS THE UPLOAD SET AND THERE IS NO OTHER. Until 2026-09-23
# nothing in this repository named one: `gh release create` was run by hand at
# RELEASING item 14, and whoever ran it chose the assets by looking at
# `Releases/` -- which is how `v1.0.0`'s seven assets became `v1.1.0`'s seven
# assets, by matching and not by deciding. Q233, asset by asset, is the
# maintainer turning that judgement into three decisions, and this list is where
# they live.
#
# WHY EACH ONE IS HERE:
#   - the installer, because it is what a person downloads and runs;
#   - the full package, because it is what every update and every rollback
#     fetches -- and since the full-packages-only decision it is the ONLY package
#     a feed ever names;
#   - `releases.<channel>.json`, because it is the feed, and it is the one file
#     a Velopack client reads.
#
# WHY THE OTHER FOUR ARE NOT, each by its own decision and not by omission:
#   - `BrowserAI.zip`, the portable archive -- the maintainer, verbatim: *"2 drop
#     and update the readme to not mention it"*. Still packed, still renamed,
#     still local.
#   - `BrowserAI-<version>-manifest.zip` -- *"7 move it"*. The resolved set is
#     committed under `docs/evidence/` per release instead, which is a copy that
#     survives a clone and not one that survives a release page.
#   - `RELEASES` and `assets.<channel>.json` -- *"5+6 execute the test but also
#     double check the velopack documentation and code"*. Both were done:
#     nothing reads either from a release, measured 2026-09-23 against a real
#     Velopack client, with the control that a feed holding ONLY those two comes
#     back *"No full / applicable release was found to download"*
#     (kb/packaging/velopack.md). ⚠️ Both stay ON DISK in `Releases/`:
#     `vpk upload` reads the LOCAL `assets.<channel>.json` to learn what to
#     upload, so not publishing a file and not producing it are different
#     changes, and only the first was decided.
#
# Names and not paths, because a release asset IS a name -- and because a
# name is what `ReleaseScriptTests` can read back out of this file. Anything
# added here must also be classified in
# `ReleaseScriptTests.NothingElseInTheReleaseDirectoryIsPublished`, which is a
# red build until somebody decides.
$uploadSet = @(
    "$downloadId$downloadSuffix.exe"
    "$packId-$PackVersion-full.nupkg"
    "releases.$Channel.json"
)

# A set naming a file that is not there is the manifest defect one directory up:
# an upload that publishes two of three reads exactly like a complete one.
# @( ) around the loop deliberately: without it a one-element set collapses to a
# scalar and `.Count` stops meaning what it reads as.
$uploadPaths = @(foreach ($name in $uploadSet) {
    $path = Join-Path $OutputDir $name

    if (-not (Test-Path -LiteralPath $path)) {
        Write-Error "The upload set names $name and $path does not exist, so this release cannot be published as declared."
        exit 1
    }

    $path
})

# ⚠️ AND THE PACKER'S OWN LIST IS MADE TO AGREE -- Q235 b, 2026-09-23.
# `vpk pack` writes `assets.<channel>.json` naming everything it produced, and
# `vpk upload github` uploads EVERY file listed in it. So the portable archive
# would have been published by the one command nobody here runs, contradicting
# the set declared above, and nothing would have said so: the two mechanisms
# never meet. This rewrites the list to the declaration and refuses if what lands
# on disk is not it. The file STAYS -- `vpk upload` needs it; what changes is
# what it names.
& (Join-Path $PSScriptRoot 'Set-UploadAssets.ps1') -Path $assets -Keep $uploadSet

if ($LASTEXITCODE -ne 0) {
    Write-Error "The packer's own asset list could not be brought in line with the declared upload set, so an upload from it would publish something this release does not."
    exit 1
}

Write-Host ''
Write-Host "This release publishes $($uploadSet.Count) assets and no others:"
foreach ($path in $uploadPaths) {
    Write-Host ("  {0}  {1:N0} bytes" -f (Split-Path -Leaf $path), (Get-Item -LiteralPath $path).Length)
}

# Emitted ready to run, so that publishing is a paste and not a judgement.
# The tag is the caller's: this script deliberately does not push, tag or publish.
Write-Host ''
Write-Host ("gh release create <tag> --title <title> --notes-file <body> " + (($uploadPaths | ForEach-Object { '"' + $_ + '"' }) -join ' '))
Write-Host ''

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
    Setup            = $setup
    Portable         = $portable
    Manifest         = (Join-Path $OutputDir "releases.$Channel.json")
    ResolvedSet      = ($manifest | Select-Object -Last 1)
    ReleaseBody      = $bodyFile
    ReleaseBodyShape = $bodyShape
    Upload           = $uploadPaths
}
