# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    The whole 127 c measurement: each client mode in each record mode, three rounds,
    one run at a time, and a table of what each run did.

.DESCRIPTION
    Run it under the suite lock, from a worktree whose slice is published. Every run is
    run.ps1, and every summary is summarize.ps1's; runs.tsv and summary.txt are written
    into the output folder.

.PARAMETER Out
    An empty folder for the runs.

.PARAMETER Relay
    The published BrowserAI.exe.

.PARAMETER Node
    The node.exe the rig's scripts run on.

.PARAMETER PrintClaude
    The Claude Code that runs `claude -p`: the installed CLI.

.PARAMETER StreamClaude
    The Claude Code that runs the stream-json transport: the binary the VS Code extension
    bundles.

.PARAMETER Rounds
    How many runs of each pair.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Relay,
    [Parameter(Mandatory)] [string] $Node,
    [Parameter(Mandatory)] [string] $PrintClaude,
    [Parameter(Mandatory)] [string] $StreamClaude,
    [int] $Rounds = 3
)

$ErrorActionPreference = 'Stop'
$null = New-Item -ItemType Directory -Force -Path $Out

$rows = foreach ($round in 1..$Rounds) {
    foreach ($client in 'print', 'stream') {
        foreach ($mode in 'same', 'differ') {
            $name = "$client-$mode-r$round"
            $claude = if ($client -eq 'print') { $PrintClaude } else { $StreamClaude }

            & (Join-Path $PSScriptRoot 'run.ps1') -Client $client -Mode $mode -Run $name -Out $Out -Claude $claude -Relay $Relay -Node $Node
            $summary = & (Join-Path $PSScriptRoot 'summarize.ps1') -Folder (Join-Path $Out $name)

            [pscustomobject]@{
                run = $name
                holds = $summary.holds
                relays = $summary.relays
                refusals = $summary.refusalsTheModelSaw
                answers = $summary.standInAnswersTheModelSaw
                passed = $summary.callsPassedPerConnection
                relay27 = $summary.relay27Lines
                sameClient = $summary.everyRelaysClientIsTheClientStarted
                oneCreation = $summary.oneClientCreationTime
                records = @($summary.records).Count
                rewritten = $summary.recordsRewritten
                problems = ($summary.problems -replace '\s+', ' ').Trim()
            }
        }
    }
}

$rows | Export-Csv -LiteralPath (Join-Path $Out 'runs.tsv') -Delimiter "`t" -NoTypeInformation -Encoding utf8NoBOM

$lines = @(
    "runs: $(@($rows).Count), holding what their mode says: $(@($rows | Where-Object holds).Count)"
    foreach ($group in $rows | Group-Object { ($_.run -replace '-r\d+$', '') }) {
        "  $($group.Name): $(@($group.Group | Where-Object holds).Count) of $($group.Count)"
    }
)

$lines | Set-Content -LiteralPath (Join-Path $Out 'summary.txt') -Encoding utf8NoBOM
$lines
