# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# usage: cprobe.sh <run-name> <server-mode> <stub-script> <waitbefore> <port> <prompt>
set -u
WU=/c/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
F=C:/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
NAME="$1"; SMODE="$2"; SCRIPT="$3"; WAITBEFORE="$4"; PORT="$5"; PROMPT="$6"
NODE="C:/Program Files/nodejs/node.exe"
CODEX="/c/Users/jori/AppData/Local/OpenAI/Codex/bin/247581e40ee272fb/codex.exe"
mkdir -p "$WU/logs" "$WU/out" "$WU/proj" "$WU/codexhome"
rm -f "$WU/logs/$NAME."* "$WU/logs/$NAME-stub."* "$WU/out/$NAME."*

cat > "$WU/codexhome/config.toml" <<CFG
model = "stub-model"
model_provider = "stub"
approval_policy = "never"
sandbox_mode = "read-only"

[model_providers.stub]
name = "stub"
base_url = "http://127.0.0.1:$PORT"
wire_api = "responses"
env_key = "STUB_KEY"

[mcp_servers.probe]
command = "C:/Program Files/nodejs/node.exe"
args = ["$F/rig/server.js"]
env = { PROBE_LOGDIR = "$F/logs", PROBE_TAG = "$NAME", PROBE_MODE = "$SMODE" }
startup_timeout_sec = 20
tool_timeout_sec = 60
CFG

export STUB_PORT="$PORT" STUB_LOGDIR="$F/logs" STUB_TAG="$NAME-stub" STUB_SCRIPT="$SCRIPT"
export STUB_WAITFILE="$F/logs/$NAME.exited" STUB_WAITBEFORE="$WAITBEFORE"
"$NODE" "$F/rig/openaistub.js" > "$WU/logs/$NAME.stub.out" 2>&1 &
SP=$!
sleep 1
export CODEX_HOME="$F/codexhome"
export STUB_KEY=not-a-real-key
export RUST_LOG=codex_core::mcp=trace,codex_rmcp_client=trace,info
timeout 180 "$CODEX" exec ${RESUME:-} --json --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox -C "$F/proj" "$PROMPT" \
  > "$WU/out/$NAME.jsonl" 2> "$WU/out/$NAME.err.txt" < /dev/null
echo "codex_exit=$?"
kill $SP 2>/dev/null
