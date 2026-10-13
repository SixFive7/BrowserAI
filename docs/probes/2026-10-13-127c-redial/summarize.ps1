# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    Reads one run of the 127 c measurement and says what happened in it.

.DESCRIPTION
    From the stand-in's log: each relay's greeting, with the relay's pid, its client's pid
    and its client's creation time, and the calls each connection was passed. From the
    model stub's request log: every tool result the model was handed, in order, which is
    what the model saw. From the relays' own logs, copied out of their data root: the line
    a relay writes when its client was answered this same list before (Relay[27]), and the
    lines of a refusal. From the data root: the records.

    Writes summary.json into the run's folder, and returns the summary.

.PARAMETER Folder
    The run's folder, as run.ps1 left it.
#>
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $Folder)

$ErrorActionPreference = 'Stop'

$refusalText = 'was NOT forwarded, once, because this connection has never asked BrowserAI for its tool list'

$run = Get-Content -LiteralPath (Join-Path $Folder 'run.json') -Raw | ConvertFrom-Json
$pidFile = Join-Path $Folder 'client.pid'
$clientPid = if (Test-Path -LiteralPath $pidFile) { [int](Get-Content -LiteralPath $pidFile -Raw) } else { 0 }
$standInLog = Join-Path $Folder 'standin.jsonl'
$events = if (Test-Path -LiteralPath $standInLog) { @(Get-Content -LiteralPath $standInLog | ForEach-Object { $_ | ConvertFrom-Json }) } else { @() }

$hellos = @($events | Where-Object { $_.event -eq 'received' -and $_.method -eq 'browserai/hello' })
$calls = @($events | Where-Object { $_.event -eq 'received' -and $_.method -eq 'tools/call' })
$rewritten = @($events | Where-Object { $_.event -eq 'record-rewritten' })

# What the model was handed: the tool results of the last request, which carries the
# whole conversation.
$requestLog = Join-Path $Folder 'model.requests.jsonl'
$requests = if (Test-Path -LiteralPath $requestLog) { @(Get-Content -LiteralPath $requestLog | ForEach-Object { $_ | ConvertFrom-Json }) } else { @() }
$results = @()

if ($requests.Count -gt 0) {
    foreach ($message in @($requests[-1].body.messages)) {
        foreach ($block in @($message.content)) {
            if ($block.type -eq 'tool_result') {
                $text = if ($block.content -is [string]) { $block.content } else { (@($block.content) | ForEach-Object { $_.text }) -join '' }
                $results += [pscustomobject]@{ isError = [bool]$block.is_error; text = $text }
            }
        }
    }
}

$refusals = @($results | Where-Object { $_.text -like "*$refusalText*" })
$answers = @($results | ForEach-Object { if ($_.text -match 'stand-in answered call (\d+) on connection (\d+)') { [pscustomobject]@{ call = [int]$Matches[1]; connection = [int]$Matches[2] } } })

$logs = Join-Path $Folder 'data-root\logs'
$lines = if (Test-Path -LiteralPath $logs) { @(Get-ChildItem -LiteralPath $logs -File | ForEach-Object { Get-Content -LiteralPath $_.FullName }) } else { @() }
$sameList = @($lines | Where-Object { $_ -match 'Relay\[27\]' })
$refusedInLog = @($lines | Where-Object { $_ -match 'never asked for the tool list' })
$records = Join-Path $Folder 'data-root\tool-lists'
$recordNames = if (Test-Path -LiteralPath $records) { @(Get-ChildItem -LiteralPath $records -File | ForEach-Object { $_.Name }) } else { @() }

$started = @($hellos | ForEach-Object { $_.clientStarted } | Sort-Object -Unique)

$summary = [pscustomobject]@{
    run = $run.run
    client = $run.client
    mode = $run.mode
    driverExit = $run.driverExit
    relays = $hellos.Count
    relayPids = @($hellos | ForEach-Object { $_.relayPid })
    everyRelaysClientIsTheClientStarted = ($hellos.Count -gt 0) -and (@($hellos | Where-Object { [int]$_.clientPid -ne $clientPid }).Count -eq 0)
    oneClientCreationTime = ($started.Count -eq 1) -and ($null -ne $started[0])
    clientCreationTime = $started -join ','
    callsPassedPerConnection = (@($calls | Group-Object connection | Sort-Object Name | ForEach-Object { "$($_.Name):$($_.Count)" }) -join ' ')
    recordsRewritten = $rewritten.Count
    toolResults = $results.Count
    refusalsTheModelSaw = $refusals.Count
    standInAnswersTheModelSaw = (@($answers | ForEach-Object { "$($_.connection)/$($_.call)" }) -join ' ')
    otherResults = @($results | Where-Object { $_.text -notlike "*$refusalText*" -and $_.text -notmatch 'stand-in answered call' } | ForEach-Object { ($_.text -replace '\s+', ' ').Substring(0, [Math]::Min(160, ($_.text -replace '\s+', ' ').Length)) })
    relay27Lines = $sameList.Count
    refusalLogLines = $refusedInLog.Count
    records = $recordNames
    problems = if (Test-Path -LiteralPath (Join-Path $Folder 'run-problem.txt')) { Get-Content -LiteralPath (Join-Path $Folder 'run-problem.txt') -Raw } else { '' }
}

$secondConnectionAnswered = @($answers | Where-Object { $_.connection -ge 2 }).Count -gt 0

$summary | Add-Member -NotePropertyName holds -NotePropertyValue $(
    if ($run.mode -eq 'same') {
        $summary.relays -ge 2 -and $summary.refusalsTheModelSaw -eq 0 -and $secondConnectionAnswered -and $summary.relay27Lines -ge 1 -and $summary.everyRelaysClientIsTheClientStarted -and $summary.oneClientCreationTime
    }
    else {
        $summary.relays -ge 2 -and $summary.refusalsTheModelSaw -eq 1 -and $secondConnectionAnswered -and $summary.relay27Lines -eq 0 -and $summary.recordsRewritten -ge 1 -and $summary.everyRelaysClientIsTheClientStarted
    })

$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Folder 'summary.json') -Encoding utf8NoBOM
$summary
