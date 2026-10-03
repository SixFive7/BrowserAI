#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# One `codex exec` run for Q296 a: a first exec whose BrowserAI starts in updating
# mode, then `codex exec resume --last` in a new process.
#
# usage: cx-exec-run.sh <run-name> <port> <server: stub|real> [stub-mode]
#
# Nothing here touches the maintainer's own state: CODEX_HOME is a scratch directory
# with a config.toml this script writes, the model provider is a local stub, and the
# CLI is the desktop app's copy under AppData\Local\OpenAI\Codex\bin (outside ~\.codex).
set -u
NAME="${1:?run name}"; PORT="${2:?port}"; SERVER="${3:?stub|real}"; SMODE="${4:-updating-first}"
W=/c/Source/SixFive7/BrowserAI/.work/client-behaviour
F=C:/Source/SixFive7/BrowserAI/.work/client-behaviour
NODE="C:/Program Files/nodejs/node.exe"
CODEX="%USERPROFILE%/AppData/Local/OpenAI/Codex/bin/247581e40ee272fb/codex.exe"
RUN="$W/runs/$NAME"; RF="$F/runs/$NAME"
rm -rf "$RUN"; mkdir -p "$RUN/home" "$RUN/proj" "$RUN/logs" "$RUN/out" "$RUN/sessions"

if [ "$SERVER" = "real" ]; then
  # The stand-in updater under the scratch app root, started before the client can start
  # the server, and ended by its own handle (standin.js) after STANDIN_LIFE_MS or on the
  # trigger file <logs>/end-the-updater.
  mkdir -p "${REAL_ROOT:?REAL_ROOT}"
  SI_ROOT="$REAL_ROOT" SI_LOGS="$RF/logs" SI_LIFE_MS="${STANDIN_LIFE_MS:-600000}" "$NODE" "$F/rig/standin.js" > "$RUN/logs/standin.out" 2>&1 &
  for i in $(seq 1 100); do [ -f "$RUN/logs/updater.pid" ] && break; sleep 0.1; done
  echo "$(date -u +%FT%T.%3NZ) stand-in updater pid=$(cat "$RUN/logs/updater.pid" 2>/dev/null)" >> "$RUN/out/meta.txt"
fi
DIR="$RF/sessions"

if [ "$SERVER" = "stub" ]; then
  SERVERTOML="command = \"C:/Program Files/nodejs/node.exe\"
args = [\"$F/rig/updstub.js\"]
env = { US_LOGDIR = \"$RF/logs\", US_TAG = \"$NAME\", US_MODE = \"$SMODE\", US_EXIT_AFTER_MS = \"${US_EXIT_AFTER_MS:-4000}\" }"
else
  SERVERTOML="command = \"C:/Program Files/nodejs/node.exe\"
args = [\"$F/rig/passthru.js\"]
env = { PT_SERVER = \"${REAL_SERVER:?REAL_SERVER}\", PT_LOGDIR = \"$RF/logs\", PT_TAG = \"$NAME\", BROWSERAI_ROOT = \"${REAL_ROOT:?REAL_ROOT}\" }"
fi

cat > "$RUN/home/config.toml" <<CFG
model = "stub-model"
model_provider = "stub"
approval_policy = "never"
sandbox_mode = "read-only"

[model_providers.stub]
name = "stub"
base_url = "http://127.0.0.1:$PORT"
wire_api = "responses"
env_key = "STUB_KEY"

[mcp_servers.browserai]
$SERVERTOML
startup_timeout_sec = 20
tool_timeout_sec = 60
CFG

EXITED="$RF/logs/$NAME.exited.1"
"$NODE" -e '
const fs=require("fs"); const [out,exited,dir]=process.argv.slice(1);
const call={tool:"mcp__browserai/browserai_list",args:{directory:dir}};
fs.writeFileSync(out, JSON.stringify([
  call,
  Object.assign({waitFile:exited,waitMaxMs:60000,delayMs:1500},call),
  Object.assign({delayMs:5000},call),
  {text:"exec one is done"}
]));
' "$RUN/script1.json" "$EXITED" "$DIR"
"$NODE" -e '
const fs=require("fs"); const [out,dir]=process.argv.slice(1);
fs.writeFileSync(out, JSON.stringify([
  {tool:"mcp__browserai/browserai_list",args:{directory:dir}},
  {text:"exec two is done"}
]));
' "$RUN/script2.json" "$DIR"

export CODEX_HOME="$RF/home"
export STUB_KEY=not-a-real-key
export RUST_LOG=codex_core::mcp=trace,codex_rmcp_client=trace,codex_mcp=trace,info
unset CLAUDECODE CLAUDE_CODE_ENTRYPOINT CLAUDE_CODE_SSE_PORT

for S in 1 2; do
  if [ "$S" = "2" ] && [ "$SERVER" = "real" ]; then
    for i in $(seq 1 600); do [ -f "$RUN/logs/updater-ended" ] && break; sleep 0.1; done
    echo "$(date -u +%FT%T.%3NZ) updater ended: $(cat "$RUN/logs/updater-ended" 2>/dev/null)" >> "$RUN/out/meta.txt"
  fi
  export STUB_PORT="$PORT" STUB_LOGDIR="$RF/logs" STUB_TAG="$NAME-s$S" STUB_SCRIPT_FILE="$RF/script$S.json"
  "$NODE" "$F/rig/cxstub.js" > "$RUN/logs/$NAME-s$S.stub.out" 2>&1 &
  SP=$!
  sleep 1
  echo "$(date -u +%FT%T.%3NZ) exec $S start" >> "$RUN/out/meta.txt"
  if [ "$S" = "1" ]; then
    timeout 240 "$CODEX" exec --json --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox -C "$RF/proj" \
      "List the BrowserAI sessions under the directory." > "$RUN/out/s$S.jsonl" 2> "$RUN/out/s$S.err.txt" < /dev/null
  else
    # The options belong to `exec`, before the subcommand: `resume` refuses -C.
    timeout 240 "$CODEX" exec --json --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox -C "$RF/proj" \
      resume --last "List the BrowserAI sessions under the directory." > "$RUN/out/s$S.jsonl" 2> "$RUN/out/s$S.err.txt" < /dev/null
  fi
  echo "$(date -u +%FT%T.%3NZ) exec $S codex exit=$?" >> "$RUN/out/meta.txt"
  kill $SP 2>/dev/null; wait $SP 2>/dev/null
  sleep 1
done
echo "--- launches ---"; cat "$RUN/logs/$NAME.launches.log" 2>/dev/null
