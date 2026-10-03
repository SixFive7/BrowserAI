# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Generates batch files for ExitRig. Usage: gen.ps1 -Batch <name> -Scenarios a,b1,... -Delays 0,100 -Reps 3
param(
  [Parameter(Mandatory)] [string] $Batch,
  [string[]] $Scenarios = @(),
  [int[]] $Delays = @(0, 100, 1000, 3000, 6000, 15000),
  [int] $Reps = 3,
  [switch] $Versions
)
$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\client-exit'
$rig = "$S\rig\bin\ExitRig.exe"
$cliClaude = "$S\bin\cli\claude.exe"
$vscClaude = "$S\bin\vscode\claude.exe"
$codex = "$S\bin\codex\codex.exe"
$proj = "$S\proj"
$node = 'C:\Program Files\nodejs\node.exe'

$baseEnv = [ordered]@{
  CLAUDE_CONFIG_DIR = "$S\cfg\claude"; CODEX_HOME = "$S\cfg\codex"; DISABLE_AUTOUPDATER = '1'
  ANTHROPIC_BASE_URL = 'http://127.0.0.1:8931'; ANTHROPIC_API_KEY = 'stub-key-not-real'
  CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC = '1'; DISABLE_TELEMETRY = '1'; DISABLE_ERROR_REPORTING = '1'
  GIT_TERMINAL_PROMPT = '0'; GCM_INTERACTIVE = 'never'; DOTNET_CLI_TELEMETRY_OPTOUT = '1'
  TEMP = "$S\tmp"; TMP = "$S\tmp"; APPDATA = "$S\appdata\roaming"; LOCALAPPDATA = "$S\appdata\local"
  STUB_KEY = 'not-a-real-key'; GIT_CEILING_DIRECTORIES = $S
}
function EnvWith($extra) { $e = [ordered]@{}; foreach ($k in $baseEnv.Keys) { $e[$k] = $baseEnv[$k] }; if ($extra) { foreach ($k in $extra.Keys) { $e[$k] = $extra[$k] } }; return $e }

$prompt = 'Call the probe ping tool, then say done.'
function McpJson($dir, $delay) {
  $o = @{ mcpServers = @{ probe = @{ type = 'stdio'; command = $rig; args = @('server', '--logdir', $dir, '--delay-ms', "$delay") } } }
  return ($o | ConvertTo-Json -Depth 6 -Compress)
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
base_url = "http://127.0.0.1:8932"
wire_api = "responses"
env_key = "STUB_KEY"

[mcp_servers.probe]
command = '$rig'
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

function StreamJsonSteps($endKind) {
  $steps = @(
    @{ do = 'send'; line = $initReq },
    @{ do = 'waitFor'; pattern = '"type":"control_response"'; timeoutMs = 30000 },
    @{ do = 'send'; line = $userMsg },
    @{ do = 'waitFor'; pattern = '"type":"result"'; timeoutMs = 90000 },
    @{ do = 'sleep'; ms = 2000 },
    @{ do = 'mark'; text = "end: $endKind" }
  )
  switch ($endKind) {
    'close' { $steps += @{ do = 'closeStdin' } }
    'terminate' { $steps += @{ do = 'terminate' } }
    'sdkclose' { $steps += @{ do = 'closeStdin' }; $steps += @{ do = 'terminateIfAliveAfter'; ms = 7000 } }
  }
  return $steps
}

$runs = @()
if ($Versions) {
  foreach ($v in @(@{n = 'claude-cli'; exe = $cliClaude; args = @('--version') }, @{n = 'claude-vscode'; exe = $vscClaude; args = @('--version') }, @{n = 'codex'; exe = $codex; args = @('--version') })) {
    $dir = "$S\runs\$Batch\$($v.n)"
    $runs += [ordered]@{ name = $v.n; dir = $dir; exe = $v.exe; args = $v.args; cwd = $proj; stdin = 'closed'; maxMs = 60000; settleMs = 5000; env = (EnvWith $null) }
  }
}
foreach ($sc in $Scenarios) {
  foreach ($d in $Delays) {
    foreach ($r in 1..$Reps) {
      $name = "$sc-d$d-r$r"
      $dir = "$S\runs\$Batch\$name"
      $spec = [ordered]@{ name = $name; dir = $dir; cwd = $proj; maxMs = 150000; settleMs = 25000 }
      switch -Regex ($sc) {
        '^a$' {
          $spec.exe = $cliClaude
          $spec.args = @('-p', '--mcp-config', "$dir\mcp.json", '--strict-mcp-config', '--tools', '', '--allowedTools', 'mcp__probe__ping', '--permission-mode', 'acceptEdits', '--output-format', 'stream-json', '--verbose', '--debug-file', "$dir\claude-debug.log", '--model', 'sonnet', $prompt)
          $spec.stdin = 'closed'
          $spec.env = EnvWith $null
          $spec.writeFiles = @(@{ path = "$dir\mcp.json"; content = (McpJson $dir $d) })
        }
        '^(b|c)(1|2|3)$' {
          $spec.exe = if ($Matches[1] -eq 'b') { $cliClaude } else { $vscClaude }
          $endKind = @{ '1' = 'close'; '2' = 'terminate'; '3' = 'sdkclose' }[$Matches[2]]
          $spec.args = @('--output-format', 'stream-json', '--verbose', '--input-format', 'stream-json', '--mcp-config', "$dir\mcp.json", '--strict-mcp-config', '--tools', '', '--allowedTools', 'mcp__probe__ping', '--permission-mode', 'acceptEdits', '--debug-file', "$dir\claude-debug.log", '--model', 'sonnet')
          $spec.stdin = 'pipe'
          $spec.env = EnvWith ([ordered]@{ CLAUDE_CODE_ENTRYPOINT = 'claude-vscode'; CLAUDE_AGENT_SDK_VERSION = '0.3.287'; CLAUDE_CODE_SDK_READS_SESSION_STATE = '1' })
          $spec.writeFiles = @(@{ path = "$dir\mcp.json"; content = (McpJson $dir $d) })
          $spec.steps = StreamJsonSteps $endKind
        }
        '^d1$' {
          $spec.exe = $codex
          $spec.args = @('exec', '--json', '--skip-git-repo-check', '--dangerously-bypass-approvals-and-sandbox', '-C', $proj, $prompt)
          $spec.stdin = 'closed'
          $spec.env = EnvWith ([ordered]@{ RUST_LOG = 'codex_core::mcp=trace,codex_rmcp_client=trace,codex_mcp=trace,info' })
          $spec.writeFiles = @(@{ path = "$S\cfg\codex\config.toml"; content = (CodexToml $dir $d) })
        }
        '^d2$' {
          $spec.exe = $codex
          $spec.args = @('app-server', '--listen', 'stdio://')
          $spec.stdin = 'pipe'
          $spec.autoReplyJsonRpc = $true
          $spec.env = EnvWith ([ordered]@{ RUST_LOG = 'codex_core::mcp=trace,codex_rmcp_client=trace,codex_mcp=trace,info' })
          $spec.writeFiles = @(@{ path = "$S\cfg\codex\config.toml"; content = (CodexToml $dir $d) })
          $spec.steps = @(
            @{ do = 'send'; line = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"exit-probe","title":"exit probe","version":"1.0.0"}}}' },
            @{ do = 'waitFor'; pattern = '"id":1\b'; timeoutMs = 30000 },
            @{ do = 'send'; line = '{"jsonrpc":"2.0","id":2,"method":"thread/start","params":{}}' },
            @{ do = 'waitFor'; pattern = '"id":2\b.*?(?:"thread":\{"id":"(?<THREAD>[^"]+)"|"threadId":"(?<THREAD>[^"]+)"|"thread_id":"(?<THREAD>[^"]+)")'; timeoutMs = 60000 },
            @{ do = 'send'; line = ('{"jsonrpc":"2.0","id":3,"method":"turn/start","params":{"threadId":"$THREAD","input":[{"type":"text","text":"' + $prompt + '"}]}}') },
            @{ do = 'waitFor'; pattern = 'turn/completed'; timeoutMs = 90000 },
            @{ do = 'sleep'; ms = 2000 },
            @{ do = 'mark'; text = 'end: close stdin' },
            @{ do = 'closeStdin' }
          )
        }
      }
      $runs += $spec
    }
  }
}
$batchObj = [ordered]@{
  progress = "$S\batches\$Batch.progress.log"; gapMs = 1500
  stubs = @(
    [ordered]@{ name = 'anthropic'; exe = $node; args = @("$S\stub\anthropic-stub.js"); env = @{ STUB_PORT = '8931'; STUB_LOG = "$S\stub\anthropic-stub.$Batch.log" } },
    [ordered]@{ name = 'openai'; exe = $node; args = @("$S\stub\openai-stub.js"); env = @{ STUB_PORT = '8932'; STUB_LOG = "$S\stub\openai-stub.$Batch.log" } }
  )
  runs = $runs
}
$batchObj | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 "$S\batches\$Batch.json"
"$Batch : $($runs.Count) runs"
