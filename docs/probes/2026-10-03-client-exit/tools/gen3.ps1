# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Third-round generator: the VS Code extension host's own transport behaviour, mimicked by
# rig/vscodehost.js running under node (the harness's root process is node, claude is its child).
#   e5  host-exit : the extension host goes away (stdin.end, then the host process exits)
#   e6  sdk-close : ProcessTransport.close() (stdin.end, SIGKILL only if alive after 2 s + 5 s)
param(
  [Parameter(Mandatory)] [string] $Batch,
  [string[]] $Scenarios = @('e5', 'e6'),
  [int[]] $Delays = @(0, 100, 1000, 3000, 6000, 15000),
  [int] $Reps = 3,
  [string] $Rig = 'C:\Source\SixFive7\BrowserAI\.work\client-exit\rig\bin2\ExitRig.exe',
  [string] $ClaudeCfg = 'C:\Source\SixFive7\BrowserAI\.work\client-exit\cfg\claude-sdk',
  [string] $AnthropicPort = '8936'
)
$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\client-exit'
$vscClaude = "$S\bin\vscode\claude.exe"
$proj = "$S\proj"
$node = 'C:\Program Files\nodejs\node.exe'
$baseEnv = [ordered]@{
  CLAUDE_CONFIG_DIR = $ClaudeCfg; CODEX_HOME = "$S\cfg\codex"; DISABLE_AUTOUPDATER = '1'
  ANTHROPIC_BASE_URL = "http://127.0.0.1:$AnthropicPort"; ANTHROPIC_API_KEY = 'stub-key-not-real'
  CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC = '1'; DISABLE_TELEMETRY = '1'; DISABLE_ERROR_REPORTING = '1'
  GIT_TERMINAL_PROMPT = '0'; GCM_INTERACTIVE = 'never'; DOTNET_CLI_TELEMETRY_OPTOUT = '1'
  TEMP = "$S\tmp"; TMP = "$S\tmp"; APPDATA = "$S\appdata\roaming"; LOCALAPPDATA = "$S\appdata\local"
  GIT_CEILING_DIRECTORIES = $S
  CLAUDE_CODE_ENTRYPOINT = 'claude-vscode'; CLAUDE_AGENT_SDK_VERSION = '0.3.287'; CLAUDE_CODE_SDK_READS_SESSION_STATE = '1'
}
$runs = @()
foreach ($sc in $Scenarios) {
  $mode = @{ e5 = 'host-exit'; e6 = 'sdk-close' }[$sc]
  foreach ($d in $Delays) {
    foreach ($r in 1..$Reps) {
      $name = "$sc-d$d-r$r"
      $dir = "$S\runs\$Batch\$name"
      $mcp = (@{ mcpServers = @{ probe = @{ type = 'stdio'; command = $Rig; args = @('server', '--logdir', $dir, '--delay-ms', "$d") } } } | ConvertTo-Json -Depth 6 -Compress)
      $cargs = @('--output-format', 'stream-json', '--verbose', '--input-format', 'stream-json', '--mcp-config', "$dir\mcp.json", '--strict-mcp-config', '--tools', '', '--allowedTools', 'mcp__probe__ping', '--permission-mode', 'acceptEdits', '--debug-file', "$dir\claude-debug.log", '--model', 'sonnet')
      $runs += [ordered]@{ name = $name; dir = $dir; exe = $node; args = @("$S\rig\vscodehost.js", $vscClaude, ($cargs | ConvertTo-Json -Compress), $mode, "$dir\vscodehost.log"); cwd = $proj; stdin = 'closed'; maxMs = 150000; settleMs = 25000; env = $baseEnv
        writeFiles = @(@{ path = "$dir\mcp.json"; content = $mcp }) }
    }
  }
}
$batchObj = [ordered]@{
  progress = "$S\batches\$Batch.progress.log"; gapMs = 1500
  stubs = @([ordered]@{ name = 'anthropic'; exe = $node; args = @("$S\stub\anthropic-stub.js"); env = @{ STUB_PORT = $AnthropicPort; STUB_LOG = "$S\stub\anthropic-stub.$Batch.log" } })
  runs = $runs
}
$batchObj | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 "$S\batches\$Batch.json"
"$Batch : $($runs.Count) runs"
