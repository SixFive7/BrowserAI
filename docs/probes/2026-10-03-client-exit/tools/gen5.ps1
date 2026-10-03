# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Fifth-round generator: the same three Codex scenarios against another Codex binary.
#   d1 exec finished   d2 app-server stdin closed   d3 app-server terminated
param(
  [Parameter(Mandatory)] [string] $Batch,
  [Parameter(Mandatory)] [string] $CodexExe,
  [string[]] $Scenarios = @('d1', 'd2', 'd3'),
  [int[]] $Delays = @(100, 15000),
  [int] $Reps = 3,
  [string] $Rig = 'C:\Source\SixFive7\BrowserAI\.work\client-exit\rig\bin3\ExitRig.exe',
  [string] $OpenAiPort = '8939'
)
$ErrorActionPreference = 'Stop'
$S = 'C:\Source\SixFive7\BrowserAI\.work\client-exit'
$proj = "$S\proj"
$node = 'C:\Program Files\nodejs\node.exe'
$env0 = [ordered]@{
  CLAUDE_CONFIG_DIR = "$S\cfg\claude"; CODEX_HOME = "$S\cfg\codex"; GIT_TERMINAL_PROMPT = '0'; GCM_INTERACTIVE = 'never'
  DOTNET_CLI_TELEMETRY_OPTOUT = '1'; TEMP = "$S\tmp"; TMP = "$S\tmp"; APPDATA = "$S\appdata\roaming"; LOCALAPPDATA = "$S\appdata\local"
  STUB_KEY = 'not-a-real-key'; GIT_CEILING_DIRECTORIES = $S
  RUST_LOG = 'codex_core::mcp=trace,codex_rmcp_client=trace,codex_mcp=trace,info'
}
$prompt = 'Call the probe ping tool, then say done.'
$runs = @([ordered]@{ name = 'codex-version'; dir = "$S\runs\$Batch\codex-version"; exe = $CodexExe; args = @('--version'); cwd = $proj; stdin = 'closed'; maxMs = 60000; settleMs = 5000; env = $env0 })
foreach ($sc in $Scenarios) {
  foreach ($d in $Delays) {
    foreach ($r in 1..$Reps) {
      $name = "$sc-d$d-r$r"
      $dir = "$S\runs\$Batch\$name"
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
args = ['server', '--logdir', '$dir', '--delay-ms', '$d']
startup_timeout_sec = 20
tool_timeout_sec = 60

[projects.'$proj']
trust_level = "trusted"
"@
      $spec = [ordered]@{ name = $name; dir = $dir; cwd = $proj; maxMs = 150000; settleMs = 25000; exe = $CodexExe; env = $env0
        writeFiles = @(@{ path = "$S\cfg\codex\config.toml"; content = $toml }) }
      $appSteps = @(
        @{ do = 'send'; line = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"exit-probe","title":"exit probe","version":"1.0.0"}}}' },
        @{ do = 'waitFor'; pattern = '"id":1\b'; timeoutMs = 30000 },
        @{ do = 'send'; line = '{"jsonrpc":"2.0","id":2,"method":"thread/start","params":{}}' },
        @{ do = 'waitFor'; pattern = '"id":2\b.*?(?:"thread":\{"id":"(?<THREAD>[^"]+)"|"threadId":"(?<THREAD>[^"]+)"|"thread_id":"(?<THREAD>[^"]+)")'; timeoutMs = 60000 },
        @{ do = 'send'; line = ('{"jsonrpc":"2.0","id":3,"method":"turn/start","params":{"threadId":"$THREAD","input":[{"type":"text","text":"' + $prompt + '"}]}}') },
        @{ do = 'waitFor'; pattern = 'turn/completed'; timeoutMs = 90000 },
        @{ do = 'sleep'; ms = 2000 })
      switch ($sc) {
        'd1' { $spec.args = @('exec', '--json', '--skip-git-repo-check', '--dangerously-bypass-approvals-and-sandbox', '-C', $proj, $prompt); $spec.stdin = 'closed' }
        'd2' { $spec.args = @('app-server', '--listen', 'stdio://'); $spec.stdin = 'pipe'; $spec.autoReplyJsonRpc = $true; $spec.steps = $appSteps + @(@{ do = 'mark'; text = 'end: close stdin' }, @{ do = 'closeStdin' }) }
        'd3' { $spec.args = @('app-server', '--listen', 'stdio://'); $spec.stdin = 'pipe'; $spec.autoReplyJsonRpc = $true; $spec.steps = $appSteps + @(@{ do = 'mark'; text = 'end: terminate' }, @{ do = 'terminate' }) }
      }
      $runs += $spec
    }
  }
}
$batchObj = [ordered]@{
  progress = "$S\batches\$Batch.progress.log"; gapMs = 1500
  stubs = @([ordered]@{ name = 'openai'; exe = $node; args = @("$S\stub\openai-stub.js"); env = @{ STUB_PORT = $OpenAiPort; STUB_LOG = "$S\stub\openai-stub.$Batch.log" } })
  runs = $runs
}
$batchObj | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 "$S\batches\$Batch.json"
"$Batch : $($runs.Count) runs"
