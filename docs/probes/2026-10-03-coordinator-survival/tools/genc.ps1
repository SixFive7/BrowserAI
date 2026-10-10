# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Option c survival probe, 2026-10-03. Every run's dummy server asks a coordinator-owned host
# (started through the Task Scheduler, outside every client's tree and job) for a browser
# stand-in, beside the stand-in it starts in its own job as today's BrowserAI does. After the
# client has gone the harness asks the host whether that browser is still alive, then releases it.
#   b1  claude 2.1.288 stream-json, stdin closed (graceful: taskkill /T /F on the server)
#   b2  claude 2.1.288 stream-json, claude terminated (its job closes)
#   e5  VS Code host stand-in exits, claude 2.1.287 (the extension's binary) in its kill-on-close job
#   d1  codex exec, d2 codex app-server stdin closed  (0.155.0-alpha.9.2)
#   n1  codex exec, n2 codex app-server stdin closed  (0.160.0)
param(
  [Parameter(Mandatory)] [string] $Batch,
  [Parameter(Mandatory)] [string] $HostPipe,
  [string[]] $Scenarios = @('b1', 'b2', 'e5', 'd1', 'd2', 'n1', 'n2'),
  [int] $Reps = 3,
  [int] $DelayMs = 15000,
  [string] $AnthropicPort = '8951',
  [string] $OpenAiPort = '8952'
)
$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\c-scratch\m1'
$X = 'C:\Source\SixFive7\BrowserAI\.work\client-exit'
$rig = "$S\rig\bin\ExitRig.exe"
$cliClaude = "$X\bin\cli\claude.exe"
$vscClaude = "$X\bin\vscode\claude.exe"
$codex155 = "$X\bin\codex\codex.exe"
$codex160 = "$X\npm-codex-0.160.0\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe"
$proj = "$S\proj"
$node = 'C:\Program Files\nodejs\node.exe'
$baseEnv = [ordered]@{
  CLAUDE_CONFIG_DIR = "$S\cfg\claude"; CODEX_HOME = "$S\cfg\codex"; DISABLE_AUTOUPDATER = '1'
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
  foreach ($r in 1..$Reps) {
    $name = "$sc-r$r"
    $dir = "$S\runs\$Batch\$name"
    $sargs = @('server', '--logdir', $dir, '--delay-ms', "$DelayMs", '--host-pipe', $HostPipe, '--host-id', "$Batch-$name")
    $mcp = (@{ mcpServers = @{ probe = @{ type = 'stdio'; command = $rig; args = $sargs } } } | ConvertTo-Json -Depth 6 -Compress)
    $argList = ($sargs | ForEach-Object { "'$_'" }) -join ', '
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
command = '$rig'
args = [$argList]
startup_timeout_sec = 20
tool_timeout_sec = 60

[projects.'$proj']
trust_level = "trusted"

[projects.'$($proj.ToLowerInvariant())']
trust_level = "trusted"
"@
    $spec = [ordered]@{ name = $name; dir = $dir; cwd = $proj; maxMs = 150000; settleMs = 25000; hostPipe = $HostPipe; hostId = "$Batch-$name" }
    switch ($sc) {
      { $_ -in 'b1', 'b2' } {
        $spec.exe = $cliClaude; $spec.stdin = 'pipe'; $spec.env = EnvWith $sjEnv
        $spec.args = @('--output-format', 'stream-json', '--verbose', '--input-format', 'stream-json', '--mcp-config', "$dir\mcp.json", '--strict-mcp-config', '--tools', '', '--allowedTools', 'mcp__probe__ping', '--permission-mode', 'acceptEdits', '--debug-file', "$dir\claude-debug.log", '--model', 'sonnet')
        $spec.writeFiles = @(@{ path = "$dir\mcp.json"; content = $mcp })
        $end = if ($sc -eq 'b1') { @{ do = 'closeStdin' } } else { @{ do = 'terminate' } }
        $spec.steps = @(@{ do = 'send'; line = $initReq }, @{ do = 'waitFor'; pattern = '"type":"control_response"'; timeoutMs = 30000 },
          @{ do = 'send'; line = $userMsg }, @{ do = 'waitFor'; pattern = '"type":"result"'; timeoutMs = 90000 },
          @{ do = 'sleep'; ms = 2000 }, @{ do = 'mark'; text = "end: $($end.do)" }, $end)
      }
      'e5' {
        $cargs = @('--output-format', 'stream-json', '--verbose', '--input-format', 'stream-json', '--mcp-config', "$dir\mcp.json", '--strict-mcp-config', '--tools', '', '--allowedTools', 'mcp__probe__ping', '--permission-mode', 'acceptEdits', '--debug-file', "$dir\claude-debug.log", '--model', 'sonnet')
        $spec.exe = $node; $spec.stdin = 'closed'; $spec.env = EnvWith $sjEnv
        $spec.args = @("$S\rig\vscodehost.js", $vscClaude, ($cargs | ConvertTo-Json -Compress), 'host-exit', "$dir\vscodehost.log")
        $spec.writeFiles = @(@{ path = "$dir\mcp.json"; content = $mcp })
      }
      { $_ -in 'd1', 'n1' } {
        $spec.exe = if ($sc -eq 'd1') { $codex155 } else { $codex160 }
        $spec.stdin = 'closed'; $spec.env = EnvWith ([ordered]@{ RUST_LOG = 'codex_core::mcp=trace,codex_rmcp_client=trace,codex_mcp=trace,info' })
        $spec.args = @('exec', '--json', '--skip-git-repo-check', '--dangerously-bypass-approvals-and-sandbox', '-C', $proj, $prompt)
        $spec.writeFiles = @(@{ path = "$S\cfg\codex\config.toml"; content = $toml })
      }
      { $_ -in 'd2', 'n2' } {
        $spec.exe = if ($sc -eq 'd2') { $codex155 } else { $codex160 }
        $spec.stdin = 'pipe'; $spec.autoReplyJsonRpc = $true; $spec.env = EnvWith ([ordered]@{ RUST_LOG = 'codex_core::mcp=trace,codex_rmcp_client=trace,codex_mcp=trace,info' })
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
