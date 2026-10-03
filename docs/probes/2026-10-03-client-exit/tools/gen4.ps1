# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Fourth-round generator: option-c probes. The dummy server starts a KEEPER outside its own job,
# either as a direct child (--keeper child) or orphaned through a launcher that exits at once
# (--keeper orphan). The keeper watches the server by handle and then needs 5 s, exiting 88.
#   ka  claude -p          kb1 stream-json, stdin closed      kb2 stream-json, claude terminated
#   kd1 codex exec         kd2 codex app-server, stdin closed
param(
  [Parameter(Mandatory)] [string] $Batch,
  [string[]] $Scenarios = @('ka', 'kb1', 'kb2', 'kd1', 'kd2'),
  [string[]] $Modes = @('orphan', 'child'),
  [int] $Reps = 3,
  [string] $Rig = 'C:\Source\SixFive7\BrowserAI\.work\client-exit\rig\bin3\ExitRig.exe',
  [string] $ClaudeCfg = 'C:\Source\SixFive7\BrowserAI\.work\client-exit\cfg\claude-sdk',
  [string] $AnthropicPort = '8937',
  [string] $OpenAiPort = '8938'
)
$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\client-exit'
$cliClaude = "$S\bin\cli\claude.exe"
$codex = "$S\bin\codex\codex.exe"
$proj = "$S\proj"
$node = 'C:\Program Files\nodejs\node.exe'
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
$userMsg = (@{ type = 'user'; session_id = ''; message = @{ role = 'user'; content = @(@{ type = 'text'; text = $prompt }) }; parent_tool_use_id = $null } | ConvertTo-Json -Depth 8 -Compress)
$initReq = '{"type":"control_request","request_id":"req_init_1","request":{"subtype":"initialize"}}'
$sjEnv = [ordered]@{ CLAUDE_CODE_ENTRYPOINT = 'claude-vscode'; CLAUDE_AGENT_SDK_VERSION = '0.3.287'; CLAUDE_CODE_SDK_READS_SESSION_STATE = '1' }
$runs = @()
foreach ($sc in $Scenarios) {
  foreach ($m in $Modes) {
    foreach ($r in 1..$Reps) {
      $name = "$sc-$m-d15000-r$r"
      $dir = "$S\runs\$Batch\$name"
      $sargs = @('server', '--logdir', $dir, '--delay-ms', '15000', '--keeper', $m)
      $mcp = (@{ mcpServers = @{ probe = @{ type = 'stdio'; command = $Rig; args = $sargs } } } | ConvertTo-Json -Depth 6 -Compress)
      $toml = @"
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
args = ['server', '--logdir', '$dir', '--delay-ms', '15000', '--keeper', '$m']
startup_timeout_sec = 20
tool_timeout_sec = 60

[projects.'$proj']
trust_level = "trusted"
"@
      $spec = [ordered]@{ name = $name; dir = $dir; cwd = $proj; maxMs = 150000; settleMs = 25000 }
      switch ($sc) {
        'ka' {
          $spec.exe = $cliClaude; $spec.stdin = 'closed'; $spec.env = EnvWith $null
          $spec.args = @('-p', '--mcp-config', "$dir\mcp.json", '--strict-mcp-config', '--tools', '', '--allowedTools', 'mcp__probe__ping', '--permission-mode', 'acceptEdits', '--output-format', 'stream-json', '--verbose', '--debug-file', "$dir\claude-debug.log", '--model', 'sonnet', $prompt)
          $spec.writeFiles = @(@{ path = "$dir\mcp.json"; content = $mcp })
        }
        { $_ -in 'kb1', 'kb2' } {
          $spec.exe = $cliClaude; $spec.stdin = 'pipe'; $spec.env = EnvWith $sjEnv
          $spec.args = @('--output-format', 'stream-json', '--verbose', '--input-format', 'stream-json', '--mcp-config', "$dir\mcp.json", '--strict-mcp-config', '--tools', '', '--allowedTools', 'mcp__probe__ping', '--permission-mode', 'acceptEdits', '--debug-file', "$dir\claude-debug.log", '--model', 'sonnet')
          $spec.writeFiles = @(@{ path = "$dir\mcp.json"; content = $mcp })
          $end = if ($sc -eq 'kb1') { @{ do = 'closeStdin' } } else { @{ do = 'terminate' } }
          $spec.steps = @(@{ do = 'send'; line = $initReq }, @{ do = 'waitFor'; pattern = '"type":"control_response"'; timeoutMs = 30000 },
            @{ do = 'send'; line = $userMsg }, @{ do = 'waitFor'; pattern = '"type":"result"'; timeoutMs = 90000 },
            @{ do = 'sleep'; ms = 2000 }, @{ do = 'mark'; text = "end: $($end.do)" }, $end)
        }
        'kd1' {
          $spec.exe = $codex; $spec.stdin = 'closed'; $spec.env = EnvWith $null
          $spec.args = @('exec', '--json', '--skip-git-repo-check', '--dangerously-bypass-approvals-and-sandbox', '-C', $proj, $prompt)
          $spec.writeFiles = @(@{ path = "$S\cfg\codex\config.toml"; content = $toml })
        }
        'kd2' {
          $spec.exe = $codex; $spec.stdin = 'pipe'; $spec.autoReplyJsonRpc = $true; $spec.env = EnvWith $null
          $spec.args = @('app-server', '--listen', 'stdio://')
          $spec.writeFiles = @(@{ path = "$S\cfg\codex\config.toml"; content = $toml })
          $spec.steps = @(
            @{ do = 'send'; line = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"exit-probe","title":"exit probe","version":"1.0.0"}}}' },
            @{ do = 'waitFor'; pattern = '"id":1\b'; timeoutMs = 30000 },
            @{ do = 'send'; line = '{"jsonrpc":"2.0","id":2,"method":"thread/start","params":{}}' },
            @{ do = 'waitFor'; pattern = '"id":2\b.*?(?:"thread":\{"id":"(?<THREAD>[^"]+)"|"threadId":"(?<THREAD>[^"]+)"|"thread_id":"(?<THREAD>[^"]+)")'; timeoutMs = 60000 },
            @{ do = 'send'; line = ('{"jsonrpc":"2.0","id":3,"method":"turn/start","params":{"threadId":"$THREAD","input":[{"type":"text","text":"' + $prompt + '"}]}}') },
            @{ do = 'waitFor'; pattern = 'turn/completed'; timeoutMs = 90000 },
            @{ do = 'sleep'; ms = 2000 }, @{ do = 'mark'; text = 'end: close stdin' }, @{ do = 'closeStdin' })
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
