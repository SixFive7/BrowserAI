# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Second-round generator (gen.ps1 is kept as the record of the cc / cx batches).
# Adds: -Rig, a Claude config dir whose seed APPROVES the stub key (so a stream-json turn
# authenticates the way an approved key does in the extension), port choice, and scenarios
#   d3  codex app-server, TERMINATED abruptly after the turn (what a host dying does)
#   e4  claude stream-json: a tool call in flight is INTERRUPTED (control_request interrupt),
#       then the session is ended by closing stdin
param(
  [Parameter(Mandatory)] [string] $Batch,
  [string[]] $Scenarios = @(),
  [int[]] $Delays = @(0, 100, 1000, 3000, 6000, 15000),
  [int] $Reps = 3,
  [string] $Rig = 'C:\Source\SixFive7\BrowserAI\.work\client-exit\rig\bin2\ExitRig.exe',
  [string] $ClaudeCfg = 'C:\Source\SixFive7\BrowserAI\.work\client-exit\cfg\claude-sdk',
  [string] $AnthropicPort = '8934',
  [string] $OpenAiPort = '8935'
)
$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\client-exit'
$cliClaude = "$S\bin\cli\claude.exe"
$vscClaude = "$S\bin\vscode\claude.exe"
$codex = "$S\bin\codex\codex.exe"
$proj = "$S\proj"
$node = 'C:\Program Files\nodejs\node.exe'

New-Item -ItemType Directory -Force $ClaudeCfg | Out-Null
if (-not (Test-Path "$ClaudeCfg\.claude.json")) {
  '{"hasCompletedOnboarding":true,"autoUpdates":false,"bypassPermissionsModeAccepted":false,"customApiKeyResponses":{"approved":["stub-key-not-real"],"rejected":[]}}' | Set-Content -Encoding utf8NoBOM "$ClaudeCfg\.claude.json"
}

$baseEnv = [ordered]@{
  CLAUDE_CONFIG_DIR = $ClaudeCfg; CODEX_HOME = "$S\cfg\codex"; DISABLE_AUTOUPDATER = '1'
  ANTHROPIC_BASE_URL = "http://127.0.0.1:$AnthropicPort"; ANTHROPIC_API_KEY = 'stub-key-not-real'
  CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC = '1'; DISABLE_TELEMETRY = '1'; DISABLE_ERROR_REPORTING = '1'
  GIT_TERMINAL_PROMPT = '0'; GCM_INTERACTIVE = 'never'; DOTNET_CLI_TELEMETRY_OPTOUT = '1'
  TEMP = "$S\tmp"; TMP = "$S\tmp"; APPDATA = "$S\appdata\roaming"; LOCALAPPDATA = "$S\appdata\local"
  STUB_KEY = 'not-a-real-key'; GIT_CEILING_DIRECTORIES = $S
}
function EnvWith($extra) { $e = [ordered]@{}; foreach ($k in $baseEnv.Keys) { $e[$k] = $baseEnv[$k] }; if ($extra) { foreach ($k in $extra.Keys) { $e[$k] = $extra[$k] } }; return $e }
$prompt = 'Call the probe ping tool, then say done.'
function McpJson($dir, $delay, $extra) {
  $a = @('server', '--logdir', $dir, '--delay-ms', "$delay") + @($extra | Where-Object { $_ })
  return (@{ mcpServers = @{ probe = @{ type = 'stdio'; command = $Rig; args = $a } } } | ConvertTo-Json -Depth 6 -Compress)
}
function CodexToml($dir, $delay) {
  return @"
model = "stub-model"
model_provider = "stub"
approval_policy = "never"
sandbox_mode = "danger-full-access"

[features]
plugins = false

[model_providers.stub]
name = "stub"
base_url = "http://127.0.0.1:$OpenAiPort"
wire_api = "responses"
env_key = "STUB_KEY"

[mcp_servers.probe]
command = '$Rig'
args = ['server', '--logdir', '$dir', '--delay-ms', '$delay']
startup_timeout_sec = 20
tool_timeout_sec = 60

[projects.'$proj']
trust_level = "trusted"

[projects.'$($proj.ToLowerInvariant())']
trust_level = "trusted"
"@
}
$userMsg = (@{ type = 'user'; session_id = ''; message = @{ role = 'user'; content = @(@{ type = 'text'; text = $prompt }) }; parent_tool_use_id = $null } | ConvertTo-Json -Depth 8 -Compress)
$initReq = '{"type":"control_request","request_id":"req_init_1","request":{"subtype":"initialize"}}'
$intReq = '{"type":"control_request","request_id":"req_int_1","request":{"subtype":"interrupt"}}'
$sjArgs = { param($dir) @('--output-format', 'stream-json', '--verbose', '--input-format', 'stream-json', '--mcp-config', "$dir\mcp.json", '--strict-mcp-config', '--tools', '', '--allowedTools', 'mcp__probe__ping', '--permission-mode', 'acceptEdits', '--debug-file', "$dir\claude-debug.log", '--model', 'sonnet') }
$sjEnv = [ordered]@{ CLAUDE_CODE_ENTRYPOINT = 'claude-vscode'; CLAUDE_AGENT_SDK_VERSION = '0.3.287'; CLAUDE_CODE_SDK_READS_SESSION_STATE = '1' }

$runs = @()
foreach ($sc in $Scenarios) {
  foreach ($d in $Delays) {
    foreach ($r in 1..$Reps) {
      $name = "$sc-d$d-r$r"
      $dir = "$S\runs\$Batch\$name"
      $spec = [ordered]@{ name = $name; dir = $dir; cwd = $proj; maxMs = 150000; settleMs = 25000 }
      switch -Regex ($sc) {
        '^(b|c)(1|2)$' {
          $spec.exe = if ($Matches[1] -eq 'b') { $cliClaude } else { $vscClaude }
          $endStep = if ($Matches[2] -eq '1') { @{ do = 'closeStdin' } } else { @{ do = 'terminate' } }
          $spec.args = & $sjArgs $dir
          $spec.stdin = 'pipe'; $spec.env = EnvWith $sjEnv
          $spec.writeFiles = @(@{ path = "$dir\mcp.json"; content = (McpJson $dir $d $null) })
          $spec.steps = @(
            @{ do = 'send'; line = $initReq }, @{ do = 'waitFor'; pattern = '"type":"control_response"'; timeoutMs = 30000 },
            @{ do = 'send'; line = $userMsg }, @{ do = 'waitFor'; pattern = '"type":"result"'; timeoutMs = 90000 },
            @{ do = 'sleep'; ms = 2000 }, @{ do = 'mark'; text = "end: $($endStep.do)" }, $endStep)
        }
        '^e4$' {
          $spec.exe = $cliClaude
          $spec.args = & $sjArgs $dir
          $spec.stdin = 'pipe'; $spec.env = EnvWith $sjEnv
          $spec.writeFiles = @(@{ path = "$dir\mcp.json"; content = (McpJson $dir $d @('--call-delay-ms', '20000')) })
          $spec.steps = @(
            @{ do = 'send'; line = $initReq }, @{ do = 'waitFor'; pattern = '"type":"control_response"'; timeoutMs = 30000 },
            @{ do = 'send'; line = $userMsg }, @{ do = 'waitFor'; pattern = '"type":"tool_use"'; timeoutMs = 60000 },
            @{ do = 'sleep'; ms = 1500 }, @{ do = 'mark'; text = 'interrupt while the tool call is in flight' },
            @{ do = 'send'; line = $intReq }, @{ do = 'waitFor'; pattern = '"type":"result"'; timeoutMs = 60000 },
            @{ do = 'sleep'; ms = 3000 }, @{ do = 'mark'; text = 'end: closeStdin' }, @{ do = 'closeStdin' })
        }
        '^d3$' {
          $spec.exe = $codex
          $spec.args = @('app-server', '--listen', 'stdio://')
          $spec.stdin = 'pipe'; $spec.autoReplyJsonRpc = $true
          $spec.env = EnvWith ([ordered]@{ RUST_LOG = 'codex_core::mcp=trace,codex_rmcp_client=trace,codex_mcp=trace,info' })
          $spec.writeFiles = @(@{ path = "$S\cfg\codex\config.toml"; content = (CodexToml $dir $d) })
          $spec.steps = @(
            @{ do = 'send'; line = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"exit-probe","title":"exit probe","version":"1.0.0"}}}' },
            @{ do = 'waitFor'; pattern = '"id":1\b'; timeoutMs = 30000 },
            @{ do = 'send'; line = '{"jsonrpc":"2.0","id":2,"method":"thread/start","params":{}}' },
            @{ do = 'waitFor'; pattern = '"id":2\b.*?(?:"thread":\{"id":"(?<THREAD>[^"]+)"|"threadId":"(?<THREAD>[^"]+)"|"thread_id":"(?<THREAD>[^"]+)")'; timeoutMs = 60000 },
            @{ do = 'send'; line = ('{"jsonrpc":"2.0","id":3,"method":"turn/start","params":{"threadId":"$THREAD","input":[{"type":"text","text":"' + $prompt + '"}]}}') },
            @{ do = 'waitFor'; pattern = 'turn/completed'; timeoutMs = 90000 },
            @{ do = 'sleep'; ms = 2000 }, @{ do = 'mark'; text = 'end: terminate the app-server' }, @{ do = 'terminate' })
        }
      }
      $runs += $spec
    }
  }
}
$batchObj = [ordered]@{
  progress = "$S\batches\$Batch.progress.log"; gapMs = 1500
  stubs = @(
    [ordered]@{ name = 'anthropic'; exe = $node; args = @("$S\stub\anthropic-stub.js"); env = @{ STUB_PORT = $AnthropicPort; STUB_LOG = "$S\stub\anthropic-stub.$Batch.log" } },
    [ordered]@{ name = 'openai'; exe = $node; args = @("$S\stub\openai-stub.js"); env = @{ STUB_PORT = $OpenAiPort; STUB_LOG = "$S\stub\openai-stub.$Batch.log" } }
  )
  runs = $runs
}
$batchObj | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 "$S\batches\$Batch.json"
"$Batch : $($runs.Count) runs"
