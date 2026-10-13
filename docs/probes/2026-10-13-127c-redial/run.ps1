# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

<#
.SYNOPSIS
    One run of the 127 c measurement: a real Claude Code starts the published relay
    itself, a stand-in background ends that relay after its first call the way an update
    ends one, and the client starts a second relay that calls before it lists.

.DESCRIPTION
    The maintainer's 151 b of 2026-10-13: the case of a client that holds the relay's own
    tool list, let through, is measured once, end to end, with a scratch
    CLAUDE_CONFIG_DIR and CODEX_HOME, and never against the installed BrowserAI. Every
    process this run starts is started windowless, and the two servers end themselves
    when a file appears, so nothing is stopped from outside. The relays' data root is a
    folder of the run's own under the profile, where a relay's data root has to be, and is
    removed once its records are copied into the run's folder.

    Run it under the suite lock: it starts BrowserAI and claude processes.

.PARAMETER Client
    "print", `claude -p`; or "stream", the stream-json transport the VS Code extension
    drives.

.PARAMETER Mode
    "same": the second relay meets the record the first one wrote. "differ": the stand-in
    writes another digest into that record before it ends the first relay.

.PARAMETER Run
    The run's name, which names its folder.

.PARAMETER Out
    The folder the run's folder is made in.

.PARAMETER Claude
    The client's executable.

.PARAMETER Relay
    The published BrowserAI.exe.

.PARAMETER Node
    The node.exe the rig's scripts run on.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('print', 'stream')] [string] $Client,
    [Parameter(Mandatory)] [ValidateSet('same', 'differ')] [string] $Mode,
    [Parameter(Mandatory)] [string] $Run,
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Claude,
    [Parameter(Mandatory)] [string] $Relay,
    [Parameter(Mandatory)] [string] $Node
)

$ErrorActionPreference = 'Stop'

$work = New-Item -ItemType Directory -Force -Path (Join-Path $Out $Run)
$cfg = New-Item -ItemType Directory -Force -Path (Join-Path $work 'cfg')
$codexHome = New-Item -ItemType Directory -Force -Path (Join-Path $work 'codexhome')
$proj = New-Item -ItemType Directory -Force -Path (Join-Path $work 'proj')
$dataRoot = Join-Path $env:LOCALAPPDATA "BrowserAI-127c-measure\$Run"
$null = New-Item -ItemType Directory -Force -Path $dataRoot
$pipe = "\\.\pipe\BrowserAI-Background-127c-$Run-$([guid]::NewGuid().ToString('N'))"
$stops = @{ Stub = (Join-Path $work 'stop-stub'); StandIn = (Join-Path $work 'stop-standin') }

# The scratch configuration: onboarded, and refusing the project servers this
# repository's .mcp.json names, which a client started under it would find.
$refused = @('microsoft-learn', 'context7', 'nuget', 'github')
$allowed = 'mcp__browserai__browserai_list'
$projectKey = $proj.FullName -replace '\\', '/'

@{
    hasCompletedOnboarding = $true
    autoUpdates = $false
    bypassPermissionsModeAccepted = $false
    projects = @{
        $projectKey = @{
            hasTrustDialogAccepted = $true
            hasCompletedProjectOnboarding = $true
            allowedTools = @()
            enableAllProjectMcpServers = $false
            enabledMcpjsonServers = @()
            disabledMcpjsonServers = $refused
        }
    }
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $cfg '.claude.json') -Encoding utf8NoBOM

@{
    enableAllProjectMcpServers = $false
    enabledMcpjsonServers = @()
    disabledMcpjsonServers = $refused
    permissions = @{ allow = @($allowed) }
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $cfg 'settings.json') -Encoding utf8NoBOM

# The registration: the published relay, started by the client with nothing between.
$mcp = Join-Path $work 'mcp.json'

@{
    mcpServers = @{
        browserai = @{
            type = 'stdio'
            command = $Relay
            args = @('--mcp', '--data-root', $dataRoot, '--pipe', $pipe)
        }
    }
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $mcp -Encoding utf8NoBOM

# A loopback port nothing listens on, taken by binding it and letting go.
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()

# Every variable the session running this script handed it that names a client, so the
# client started here is configured by this run alone.
$inherited = @([System.Environment]::GetEnvironmentVariables().Keys | Where-Object { $_ -match '^(CLAUDE|ANTHROPIC|CODEX|AI_AGENT|BROWSERAI)' })

# One process, windowless, through cmd.exe, whose redirection writes its output to a file.
function Start-Windowless([string] $Executable, [string[]] $Arguments, [hashtable] $Variables, [string] $Log) {
    $start = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $env:SystemRoot 'System32' 'cmd.exe'))
    $line = (@($Executable) + $Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $start.Arguments = '/d /s /c "' + $line + ' > "' + $Log + '" 2>&1"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $work.FullName

    foreach ($name in $inherited) {
        $null = $start.Environment.Remove($name)
    }

    foreach ($name in $Variables.Keys) {
        $start.Environment[$name] = [string]$Variables[$name]
    }

    return [System.Diagnostics.Process]::Start($start)
}

$call = "tool:$($allowed):" + (@{ directory = $proj.FullName } | ConvertTo-Json -Compress)
$script = (@($call, $call, $call, $call, 'text:done') -join '||')

$stub = Start-Windowless $Node @((Join-Path $PSScriptRoot 'modelstub.js')) @{
    STUB_PORT = $port
    STUB_LOGDIR = $work.FullName
    STUB_TAG = 'model'
    STUB_SCRIPT = $script
    STUB_STOP = $stops.Stub
} (Join-Path $work 'modelstub.out.log')

$standIn = Start-Windowless $Node @((Join-Path $PSScriptRoot 'standin.js')) @{
    STANDIN_PIPE = $pipe
    STANDIN_LOG = (Join-Path $work 'standin.jsonl')
    STANDIN_MODE = $Mode
    STANDIN_DATA_ROOT = $dataRoot
    STANDIN_STOP = $stops.StandIn
} (Join-Path $work 'standin.out.log')

# Both are listening before the client starts.
$deadline = [DateTime]::UtcNow.AddSeconds(30)
while ([DateTime]::UtcNow -lt $deadline) {
    $stubUp = (Test-Path (Join-Path $work 'model.events.log')) -and (Select-String -LiteralPath (Join-Path $work 'model.events.log') -Pattern 'LISTENING' -Quiet)
    $standInUp = (Test-Path (Join-Path $work 'standin.jsonl')) -and (Select-String -LiteralPath (Join-Path $work 'standin.jsonl') -Pattern '"listening"' -Quiet)
    if ($stubUp -and $standInUp) { break }
    Start-Sleep -Milliseconds 200
}

$common = @('--mcp-config', $mcp, '--strict-mcp-config', '--tools', '', '--allowedTools', $allowed, '--permission-mode', 'acceptEdits', '--debug-file', (Join-Path $work 'claude-debug.log'), '--model', 'sonnet')
$arguments = if ($Client -eq 'print') {
    @('-p', '--output-format', 'stream-json', '--verbose') + $common + @('Use the browserai tools.')
}
else {
    @('--output-format', 'stream-json', '--verbose', '--input-format', 'stream-json') + $common
}

$clientVariables = @{
    CLAUDE_CONFIG_DIR = $cfg.FullName
    CODEX_HOME = $codexHome.FullName
    ANTHROPIC_BASE_URL = "http://127.0.0.1:$port"
    ANTHROPIC_API_KEY = 'stub-key-not-real'
    CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC = '1'
    DISABLE_AUTOUPDATER = '1'
    DRIVER_CLAUDE = $Claude
    DRIVER_MODE = $Client
    DRIVER_ARGS = (ConvertTo-Json -InputObject $arguments -Compress)
    DRIVER_CWD = $proj.FullName
    DRIVER_OUT = $work.FullName
    DRIVER_BOUND_MS = '180000'
}

if ($Client -eq 'stream') {
    # What the VS Code extension sets for the binary it starts.
    $clientVariables['CLAUDE_CODE_ENTRYPOINT'] = 'claude-vscode'
}

$driver = Start-Windowless $Node @((Join-Path $PSScriptRoot 'driver.js')) $clientVariables (Join-Path $work 'driver.out.log')

if (-not $driver.WaitForExit(240000)) {
    'the driver did not end within 240 s' | Set-Content -LiteralPath (Join-Path $work 'run-problem.txt')
}

# The servers end themselves.
foreach ($stop in $stops.Values) {
    Set-Content -LiteralPath $stop -Value 'stop'
}

$null = $stub.WaitForExit(15000)
$null = $standIn.WaitForExit(15000)

# The relays' records, copied into the run's folder, and their data root removed, once no
# relay of this run is left: each named its pid in its greeting.
$relays = @(Get-Content -LiteralPath (Join-Path $work 'standin.jsonl') | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.event -eq 'received' -and $_.method -eq 'browserai/hello' } | ForEach-Object { [int]$_.relayPid })
$deadline = [DateTime]::UtcNow.AddSeconds(15)
while ([DateTime]::UtcNow -lt $deadline -and @($relays | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Relay } }).Count -gt 0) {
    Start-Sleep -Milliseconds 200
}

$left = @($relays | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Relay } })
if ($left.Count -gt 0) {
    "relays still running: $($left -join ', '); the data root is left at $dataRoot" | Add-Content -LiteralPath (Join-Path $work 'run-problem.txt')
}
else {
    Copy-Item -LiteralPath $dataRoot -Destination (Join-Path $work 'data-root') -Recurse -Force
    Remove-Item -LiteralPath $dataRoot -Recurse -Force
}

[pscustomobject]@{
    run = $Run
    client = $Client
    mode = $Mode
    claude = $Claude
    relay = $Relay
    pipe = $pipe
    port = $port
    driverExit = $driver.ExitCode
    relays = $relays
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'run.json') -Encoding utf8NoBOM
