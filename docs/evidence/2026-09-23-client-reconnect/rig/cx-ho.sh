# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# Codex against the handover or relay shape.
# usage: cx-ho.sh <run-name> <server-js-abs> <stub-script> <swap-before-turn> <port> <driver> [steps-json]
#   driver = exec | appserver
set -u
WU=/c/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
F=C:/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
NAME="$1"; SERVERJS="$2"; SCRIPT="$3"; SWAPBEFORE="$4"; PORT="$5"; DRIVER="$6"; STEPS="${7:-}"
NODE="C:/Program Files/nodejs/node.exe"
CODEX="/c/Users/jori/AppData/Local/OpenAI/Codex/bin/247581e40ee272fb/codex.exe"
mkdir -p "$WU/logs" "$WU/out" "$WU/proj" "$WU/codexhome"
rm -f "$WU/logs/$NAME."* "$WU/logs/$NAME-stub."* "$WU/out/$NAME."*
printf '1.0.0' > "$WU/ho-install/VERSION"

cat > "$WU/codexhome/config.toml" <<CFG
model = "stub-model"
model_provider = "stub"
approval_policy = "never"
sandbox_mode = "danger-full-access"

[model_providers.stub]
name = "stub"
base_url = "http://127.0.0.1:$PORT"
wire_api = "responses"
env_key = "STUB_KEY"

[mcp_servers.probe]
command = "C:/Program Files/nodejs/node.exe"
args = ["$SERVERJS"]
startup_timeout_sec = 25
tool_timeout_sec = 45

[mcp_servers.probe.env]
HO_LOGDIR = "$F/logs"
HO_TAG = "$NAME"
HO_INSTALL_DIR = "$F/ho-install"
HO_HELPER_DIR = "$F/ho-helper"
HO_STATE = "$F/logs/$NAME.state.json"
HO_APPLY_DONE = "$F/logs/$NAME.apply.done"
HO_SWAP_REQUEST = "$F/logs/$NAME.swap.request"
HO_SWAP_DONE = "$F/logs/$NAME.swap.done"
HO_TRIGGER = "after-first-call"
HO_ANNOUNCE_LIST_CHANGED = "${HO_ANNOUNCE_LIST_CHANGED:-0}"
HO_CALL_DELAY_MS = "${HO_CALL_DELAY_MS:-0}"
HO_SWAP_ON_CALL = "${HO_SWAP_ON_CALL:-0}"
CFG

export STUB_PORT="$PORT" STUB_LOGDIR="$F/logs" STUB_TAG="$NAME-stub" STUB_SCRIPT="$SCRIPT"
export STUB_WAITFILE="" STUB_WAITBEFORE=-1
export STUB_SWAP_BEFORE="$SWAPBEFORE"
export STUB_SWAP_REQUEST="$F/logs/$NAME.swap.request"
export STUB_APPLY_DONE="$F/logs/$NAME.apply.done"
export STUB_SWAP_DONE="$F/logs/$NAME.swap.done"
export STUB_VERSION_FILE="$F/ho-install/VERSION"
export STUB_SWAP_WAIT_MS=8000
"$NODE" "$F/rig/openaistub.js" > "$WU/logs/$NAME.stub.out" 2>&1 &
SP=$!
sleep 1
export CODEX_HOME="$F/codexhome"
export STUB_KEY=not-a-real-key
export RUST_LOG=codex_core::mcp=trace,codex_rmcp_client=trace,info
START=$(date +%s)
if [ "$DRIVER" = "exec" ]; then
  timeout 300 "$CODEX" exec --json --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox \
    -C "$F/proj" "Call the ping tool as instructed." \
    > "$WU/out/$NAME.jsonl" 2> "$WU/out/$NAME.err.txt" < /dev/null
  RC=$?
else
  export DRIVER_LOG="$F/logs/$NAME.driver.log"
  export CODEX_EXE="$CODEX"
  "$NODE" "$F/rig/appserver.js" "$(cat "$STEPS")" > "$WU/logs/$NAME.driver.out" 2>&1
  RC=$?
fi
END=$(date +%s)
kill $SP 2>/dev/null
echo "--- $NAME exit=$RC elapsed=$((END-START))s"
