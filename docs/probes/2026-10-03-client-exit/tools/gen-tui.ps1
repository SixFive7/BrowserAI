# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Generates ConPTY (interactive TUI) batches, and the stream-json interrupt batch.
# Usage: gen-tui.ps1 -Batch <name> -Scenarios e1,e2,e3,e4 -Delays 0,100 -Reps 3 [-Explore]
param(
  [Parameter(Mandatory)] [string] $Batch,
  [string[]] $Scenarios = @(),
  [int[]] $Delays = @(0, 100, 1000, 3000, 6000, 15000),
  [int] $Reps = 3,
  [switch] $Explore,
  [string] $ClaudeExe = 'C:\Source\SixFive7\BrowserAI\.work\client-exit\bin\cli\claude.exe',
  [string] $Tag = 'cli'
)
$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\client-exit'
$rig = "$S\rig\bin2\ExitRig.exe"
$proj = "$S\proj"
$node = 'C:\Program Files\nodejs\node.exe'
$cfg = "$S\cfg\claude-tui"
$port = '8933'

$baseEnv = [ordered]@{
  CLAUDE_CONFIG_DIR = $cfg; CODEX_HOME = "$S\cfg\codex"; DISABLE_AUTOUPDATER = '1'
  ANTHROPIC_BASE_URL = "http://127.0.0.1:$port"; ANTHROPIC_API_KEY = 'stub-key-not-real'
  CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC = '1'; DISABLE_TELEMETRY = '1'; DISABLE_ERROR_REPORTING = '1'
  GIT_TERMINAL_PROMPT = '0'; GCM_INTERACTIVE = 'never'; DOTNET_CLI_TELEMETRY_OPTOUT = '1'
  TEMP = "$S\tmp"; TMP = "$S\tmp"; APPDATA = "$S\appdata\roaming"; LOCALAPPDATA = "$S\appdata\local"
  GIT_CEILING_DIRECTORIES = $S; TERM = 'xterm-256color'
}
function EnvWith($extra) { $e = [ordered]@{}; foreach ($k in $baseEnv.Keys) { $e[$k] = $baseEnv[$k] }; if ($extra) { foreach ($k in $extra.Keys) { $e[$k] = $extra[$k] } }; return $e }
$prompt = 'Call the probe ping tool.'
function McpJson($dir, $delay) { return (@{ mcpServers = @{ probe = @{ type = 'stdio'; command = $rig; args = @('server', '--logdir', $dir, '--delay-ms', "$delay") } } } | ConvertTo-Json -Depth 6 -Compress) }

# Seed: the client's own throwaway recipe plus the three things an interactive start asks about.
$seed = [ordered]@{
  hasCompletedOnboarding = $true; autoUpdates = $false; bypassPermissionsModeAccepted = $false
  customApiKeyResponses = @{ approved = @('stub-key-not-real'); rejected = @() }
  lastReleaseNotesSeen = '2.1.288'
  projects = [ordered]@{
    ($proj.Replace('\', '/')) = @{ hasTrustDialogAccepted = $true; hasCompletedProjectOnboarding = $true; allowedTools = @() }
  }
}
New-Item -ItemType Directory -Force $cfg | Out-Null
if (-not (Test-Path "$cfg\.claude.json")) { $seed | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8NoBOM "$cfg\.claude.json" }

$tuiArgs = { param($dir) @('--mcp-config', "$dir\mcp.json", '--strict-mcp-config', '--tools', '', '--allowedTools', 'mcp__probe__ping', '--permission-mode', 'acceptEdits', '--debug-file', "$dir\claude-debug.log", '--model', 'sonnet') }
$runs = @()
if ($Explore) {
  $dir = "$S\runs\$Batch\explore-$Tag"
  $runs += [ordered]@{ name = "explore-$Tag"; dir = $dir; exe = $ClaudeExe; args = (& $tuiArgs $dir); cwd = $proj; console = 'pty'; ptyNullStdHandles = $true; maxMs = 90000; settleMs = 25000; env = (EnvWith $null)
    writeFiles = @(@{ path = "$dir\mcp.json"; content = (McpJson $dir 15000) })
    steps = @(@{ do = 'sleep'; ms = 8000 }, @{ do = 'mark'; text = 'typing prompt' }, @{ do = 'type'; text = $prompt }, @{ do = 'sleep'; ms = 500 }, @{ do = 'type'; text = "`r" }, @{ do = 'sleep'; ms = 8000 }, @{ do = 'mark'; text = 'typing /exit' }, @{ do = 'type'; text = '/exit' }, @{ do = 'sleep'; ms = 500 }, @{ do = 'type'; text = "`r" }) }
}
foreach ($sc in $Scenarios) {
  foreach ($d in $Delays) {
    foreach ($r in 1..$Reps) {
      $name = "$sc-$Tag-d$d-r$r"
      $dir = "$S\runs\$Batch\$name"
      $common = @(
        @{ do = 'waitScreen'; pattern = 'for shortcuts|bypass permissions|accept edits'; timeoutMs = 30000 },
        @{ do = 'sleep'; ms = 1500 },
        @{ do = 'type'; text = $prompt }, @{ do = 'sleep'; ms = 400 }, @{ do = 'type'; text = "`r" },
        @{ do = 'waitScreen'; pattern = 'stub-turn-finished'; timeoutMs = 60000 },
        @{ do = 'sleep'; ms = 2000 }
      )
      switch ($sc) {
        'e1' { $end = @(@{ do = 'mark'; text = 'end: /exit' }, @{ do = 'type'; text = '/exit' }, @{ do = 'sleep'; ms = 400 }, @{ do = 'type'; text = "`r" }) }
        'e2' { $end = @(@{ do = 'mark'; text = 'end: ctrl-c twice' }, @{ do = 'type'; text = "$([char]3)" }, @{ do = 'sleep'; ms = 600 }, @{ do = 'type'; text = "$([char]3)" }) }
        'e3' { $end = @(@{ do = 'mark'; text = 'end: close the terminal (ClosePseudoConsole)' }, @{ do = 'closePty' }) }
      }
      $runs += [ordered]@{ name = $name; dir = $dir; exe = $ClaudeExe; args = (& $tuiArgs $dir); cwd = $proj; console = 'pty'; ptyNullStdHandles = $true; maxMs = 120000; settleMs = 25000; env = (EnvWith $null)
        writeFiles = @(@{ path = "$dir\mcp.json"; content = (McpJson $dir $d) }); steps = ($common + $end) }
    }
  }
}
$batchObj = [ordered]@{
  progress = "$S\batches\$Batch.progress.log"; gapMs = 1500
  stubs = @([ordered]@{ name = 'anthropic'; exe = $node; args = @("$S\stub\anthropic-stub.js"); env = @{ STUB_PORT = $port; STUB_LOG = "$S\stub\anthropic-stub.$Batch.log" } })
  runs = $runs
}
$batchObj | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 "$S\batches\$Batch.json"
"$Batch : $($runs.Count) runs"
