# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
The blank-page rig's launcher: one stage, under the suite's lock.

.DESCRIPTION
Checks the rig's own files before anything else, then takes the machine's suite lock and
proves the take, reads the values Firefox keeps under HKCU\Software\Mozilla\Firefox, copies
the browsers the payload pins into a scratch browsers root unless they are there already,
and runs each step of the stage as one node process in a job object of its own, with
KILL_ON_JOB_CLOSE, through the restore-stall rig's RigJob.cs. The node process waits for a
go file that this script writes only once the process is in the job. When the step ends
the job is ended, the Firefox launcher values the run added are taken out again and every
other change is reported, and the lock is released, on failure too.

Stages: 'blank' runs blank.mjs once per arm per round, arms interleaved in every round;
'repro' runs repro.mjs once, the reproduction for the report to Playwright.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('blank', 'repro')] [string] $Stage,
    [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9-]+$')] [string] $Name,
    [string] $Arms = 'one-dead,one-alive,three-mixed,none,fresh',
    [int] $Rounds = 3,
    [string] $Payload,
    [string] $Scratch = (Join-Path $env:USERPROFILE 'Downloads\tmp-restore-stall'),
    [int] $CensusSeconds = 5,
    [int] $LockWaitMinutes = 240
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'never'

$rig = $PSScriptRoot
$sibling = Join-Path (Split-Path -Parent $rig) '2026-10-13-restore-stall'
$repo = (Resolve-Path -LiteralPath (Join-Path $rig '..\..\..')).Path
$work = Join-Path $repo '.work\blank-page'
$batch = Join-Path $work "runs\$Name"

if (Test-Path -LiteralPath $batch) {
    throw "The batch folder $batch exists already. A batch is never written twice: pick a new -Name."
}

New-Item -ItemType Directory -Force -Path $batch | Out-Null
$launcherLog = Join-Path $batch 'launcher.log'

function Write-Line([string] $text) {
    $line = "$([DateTime]::UtcNow.ToString('o')) $text"
    Add-Content -LiteralPath $launcherLog -Value $line -Encoding utf8
    $line
}

if (-not $Payload) { $Payload = Join-Path $repo 'payload' }

$nodeExe = Join-Path $Payload 'node\node.exe'
$cli = Join-Path $Payload 'mcp\node_modules\@playwright\mcp\cli.js'
$playwrightCore = Join-Path $Payload 'mcp\node_modules\playwright-core'
$browsersJson = Join-Path $playwrightCore 'browsers.json'
$template = Join-Path $repo 'docs\evidence\2026-10-13-restore-stall\first\measure\configs\ff-alive-1.json'

foreach ($required in $nodeExe, $cli, $browsersJson, $template, (Join-Path $sibling 'RigJob.cs'), (Join-Path $sibling 'lib.mjs')) {
    if (-not (Test-Path -LiteralPath $required)) { throw "There is no $required; nothing was run." }
}

# 1. The rig's own files, before the lock: a C# or JavaScript mistake costs nobody the lock.
Add-Type -Path (Join-Path $sibling 'RigJob.cs')

foreach ($script in 'blank.mjs', 'repro.mjs', 'summarize.mjs') {
    & $nodeExe --check (Join-Path $rig $script)
    if ($LASTEXITCODE -ne 0) { throw "node --check failed for $script; nothing was run." }
}

# 2. The lock is the main checkout's, shared by every worktree.
$common = (& git -C $repo rev-parse --path-format=absolute --git-common-dir).Trim()
$locks = Join-Path (Split-Path -Parent $common) '.work\locks'

if (-not (Test-Path -LiteralPath $locks)) {
    throw "There is no lock folder at $locks, so this is not the machine the rig was written for; nothing was run."
}

$lock = Join-Path $locks 'suite'
$ownerFile = Join-Path $lock 'owner.txt'
$owner = "firefox-restore $([DateTime]::UtcNow.ToString('o')) blank-page rig, stage $Stage, batch $Name, pid=$PID"
$taken = $false

function Enter-SuiteLock {
    $deadline = [DateTime]::UtcNow.AddMinutes($LockWaitMinutes)

    while (-not [RigJob]::TryCreateDirectory($lock)) {
        if ([DateTime]::UtcNow -gt $deadline) {
            throw "The suite lock at $lock stayed taken for $LockWaitMinutes minutes; nothing was run."
        }

        $holder = if (Test-Path -LiteralPath $ownerFile) { [string](Get-Content -LiteralPath $ownerFile -Raw) } else { '(no owner.txt)' }
        Write-Line "the suite lock is taken, by: $holder; trying again in a minute" | Out-Null
        Start-Sleep -Seconds 60
    }

    try {
        Set-Content -LiteralPath $ownerFile -Value $owner -Encoding utf8
    }
    catch {
        # The directory is this run's, since it made it, and without an owner it would read
        # as a phantom lock to everyone else.
        [void][RigJob]::RemoveEmptyDirectory($lock)
        throw
    }

    $read = [string](Get-Content -LiteralPath $ownerFile -Raw)

    if (-not ($read.Contains('firefox-restore') -and $read.Contains("pid=$PID"))) {
        throw "The lock's owner.txt does not name this run after the take, it reads: $read. Nothing was run and the lock is left as it is."
    }
}

function Exit-SuiteLock {
    $read = if (Test-Path -LiteralPath $ownerFile) { [string](Get-Content -LiteralPath $ownerFile -Raw) } else { '' }

    if ($read.Contains('firefox-restore') -and $read.Contains("pid=$PID")) {
        Remove-Item -LiteralPath $ownerFile -Force
        if (-not [RigJob]::RemoveEmptyDirectory($lock)) { Write-Line "the lock directory $lock could not be removed" }
        Write-Line 'released the suite lock'
    }
    else {
        Write-Line "the lock no longer names this run and is left as it is; it reads: $read"
    }
}

# 3. What Firefox keeps in the registry, keyed by the executable path it ran from.
$mozilla = 'HKCU:\Software\Mozilla\Firefox'

function Read-MozillaValues {
    $values = [ordered]@{}

    foreach ($key in 'Launcher', 'TaskBarIDs', 'PreXULSkeletonUISettings', 'DllPrefetchExperiment') {
        $path = Join-Path $mozilla $key
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $item = Get-Item -LiteralPath $path
        foreach ($value in $item.Property) { $values["$key|$value"] = [string]$item.GetValue($value) }
    }

    $values
}

function Restore-MozillaValues($before, [string] $label) {
    $after = Read-MozillaValues
    $added = @($after.Keys | Where-Object { -not $before.Contains($_) })
    $changed = @($after.Keys | Where-Object { $before.Contains($_) -and $before[$_] -ne $after[$_] })
    $gone = @($before.Keys | Where-Object { -not $after.Contains($_) })
    $report = Join-Path $batch "registry-$label.txt"
    $lines = [System.Collections.Generic.List[string]]::new()

    foreach ($entry in $added) {
        $key, $value = $entry -split '\|', 2
        $ours = $key -eq 'Launcher' -and $value.StartsWith($Scratch, [StringComparison]::OrdinalIgnoreCase)

        if ($ours) {
            Remove-ItemProperty -LiteralPath (Join-Path $mozilla $key) -Name $value
            $lines.Add("added and removed: $entry = $($after[$entry])")
        }
        else {
            $lines.Add("added and left in place, not this rig's to remove: $entry = $($after[$entry])")
        }
    }

    foreach ($entry in $changed) { $lines.Add("changed, left in place: $entry was $($before[$entry]) is $($after[$entry])") }
    foreach ($entry in $gone) { $lines.Add("gone, not put back: $entry was $($before[$entry])") }
    if ($lines.Count -eq 0) { $lines.Add('no value was added, changed or removed') }

    Set-Content -LiteralPath $report -Value $lines -Encoding utf8
    Write-Line "registry after ${label}: $($lines.Count) line(s) in $report" | Out-Null
}

# 4. The scratch browsers root, shared with the restore-stall rig so that its copies are used
#    again and not made twice. Read from the provisioned root, written only into the scratch one.
$browsers = Join-Path $Scratch 'data-root\browsers'
$registry = Join-Path $Scratch 'pw-registry'
$sourceBrowsers = Join-Path $env:LOCALAPPDATA 'BrowserAI\browsers'

function Copy-PinnedBrowsers {
    New-Item -ItemType Directory -Force -Path $browsers, $registry | Out-Null
    $pins = @((Get-Content -LiteralPath $browsersJson -Raw | ConvertFrom-Json).browsers |
        Where-Object { $_.name -in @('chromium', 'firefox', 'ffmpeg', 'winldd') })
    $copied = [System.Collections.Generic.List[string]]::new()

    foreach ($pin in $pins) {
        $folder = "$($pin.name.Replace('-', '_'))-$($pin.revision)"
        $source = Join-Path $sourceBrowsers $folder
        $target = Join-Path $browsers $folder

        if (Test-Path -LiteralPath (Join-Path $target 'INSTALLATION_COMPLETE')) {
            $copied.Add("$folder already in the scratch root")
            continue
        }

        if (-not (Test-Path -LiteralPath (Join-Path $source 'INSTALLATION_COMPLETE'))) {
            throw "The provisioned browsers root has no complete $folder at $source. This rig copies, it never downloads."
        }

        & robocopy $source $target /E /COPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed with $LASTEXITCODE copying $folder" }
        $copied.Add("$folder copied from $source")
    }

    Set-Content -LiteralPath (Join-Path $batch 'browsers.txt') -Value $copied -Encoding utf8
}

# 5. One step: a node process in a job of its own, and the census of that job.

# Start-Process joins its arguments with spaces and quotes none of them, so the command
# line is composed here by the rules CommandLineToArgvW reads it back with.
function ConvertTo-CommandLine([string[]] $values) {
    ($values | ForEach-Object {
        if ($_ -notmatch '[\s"]') {
            $_
        }
        else {
            $escaped = $_ -replace '(\\*)"', '$1$1\"'
            $escaped = $escaped -replace '(\\+)$', '$1$1'
            '"' + $escaped + '"'
        }
    }) -join ' '
}

function Invoke-Step([string] $out, [string[]] $arguments) {
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $go = Join-Path $out 'go.txt'
    $census = Join-Path $out 'census.jsonl'
    $job = [RigJob]::Create()
    $exitCode = $null

    try {
        $node = Start-Process -FilePath $nodeExe -ArgumentList (ConvertTo-CommandLine ($arguments + @('--go', $go))) -WorkingDirectory $out `
            -NoNewWindow -PassThru `
            -RedirectStandardOutput (Join-Path $out 'node.stdout.txt') -RedirectStandardError (Join-Path $out 'node.stderr.txt')
        $refused = [RigJob]::AssignHandle($job, $node.Handle)

        if ($refused -ne 0) {
            # It has started nothing yet: it waits for the go file before it does anything.
            [void][RigJob]::EndStarted($node.Handle)
            throw "AssignProcessToJobObject refused the step's node process with $refused; it was ended before it started anything."
        }

        Set-Content -LiteralPath $go -Value "in the step's job since $([DateTime]::UtcNow.ToString('o'))" -Encoding utf8
        Write-Line "step started: node pid $($node.Id), output $out" | Out-Null

        while (-not $node.HasExited) {
            Add-Content -LiteralPath $census -Value ([RigJob]::Snapshot($job)) -Encoding utf8
            Start-Sleep -Seconds $CensusSeconds
        }

        $exitCode = $node.ExitCode
        Add-Content -LiteralPath $census -Value ([RigJob]::Snapshot($job)) -Encoding utf8
    }
    finally {
        $left = [RigJob]::Terminate($job)
        Write-Line "step ended: node exit $exitCode, $left process(es) of the job were still running and were ended with it" | Out-Null
    }

    $exitCode
}

$mozillaBefore = $null

# The blocks every step inherits: the scratch browsers root and registry, and the one variable
# the payload's child is started with and playwright-core 1.64.0-alpha does not set itself.
$env:PLAYWRIGHT_BROWSERS_PATH = $browsers
$env:PWTEST_SERVER_REGISTRY = $registry
$env:MOZ_DISABLE_SAFE_MODE_KEY = '1'

try {
    Enter-SuiteLock
    $taken = $true
    Write-Line "took the suite lock as: $owner" | Out-Null

    $mozillaBefore = Read-MozillaValues
    ($mozillaBefore.GetEnumerator() | ForEach-Object { "$($_.Key) = $($_.Value)" }) |
        Set-Content -LiteralPath (Join-Path $batch 'registry-before.txt') -Encoding utf8

    Copy-PinnedBrowsers

    Set-Content -LiteralPath (Join-Path $batch 'build.txt') -Encoding utf8 -Value @(
        "payload=$Payload",
        "playwrightCore=$((Get-Content -LiteralPath (Join-Path $playwrightCore 'package.json') -Raw | ConvertFrom-Json).version)",
        "commit=$((& git -C $repo rev-parse HEAD).Trim())",
        "stage=$Stage arms=$Arms rounds=$Rounds"
    )

    if ($Stage -eq 'blank') {
        $index = 0

        for ($round = 1; $round -le $Rounds; $round++) {
            foreach ($arm in ($Arms -split ',')) {
                $index++
                $out = Join-Path $batch ("{0:d2}-{1}-{2}" -f $index, $arm, $round)
                $arguments = @(
                    (Join-Path $rig 'blank.mjs'),
                    '--cli', $cli, '--browsers', $browsers, '--registry', $registry, '--template', $template,
                    '--arm', $arm, '--round', "$round", '--out', $out
                )
                $code = Invoke-Step $out $arguments
                Write-Line "repetition $index ($arm, round $round) exit code: $code" | Out-Null
            }
        }
    }
    else {
        $out = Join-Path $batch 'repro'
        $code = Invoke-Step $out @((Join-Path $rig 'repro.mjs'), '--playwright-core', $playwrightCore, '--out', $out)
        Write-Line "repro exit code: $code" | Out-Null
    }
}
finally {
    if ($null -ne $mozillaBefore) { Restore-MozillaValues $mozillaBefore $Stage }
    if ($taken) { Exit-SuiteLock }
}
