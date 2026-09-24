# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# Drives the REAL Codex CLI against the REAL published BrowserAI and reads what
# arrives before the first tools/call.
#
# usage: cx-q261.sh <run-name>
#
# What this establishes is one thing: a Codex thread asks for the tool list at
# first connect, so Q261's refusal never fires for it. It is driven through
# `codex app-server`, which needs no model at all -- `mcpServer/tool/call` makes
# the client connect and call without a turn -- so there is no API stub, no
# credential and no inference anywhere.
#
# Nothing here touches the maintainer's own state:
#   CODEX_HOME      a scratch directory with a config.toml this script writes
#   BROWSERAI_ROOT  a scratch app root under the user's profile
# The CLI is taken from AppData\Local\OpenAI\Codex\bin, which is outside
# ~\.codex, and ~\.codex is neither read as configuration nor written.
set -u

NAME="${1:?run name}"

REPO=/c/Source/SixFive7/BrowserAI
W=$REPO/.work/q261-2026-09-24
F=C:/Source/SixFive7/BrowserAI/.work/q261-2026-09-24
NODE="C:/Program Files/nodejs/node.exe"
RIG=C:/Source/SixFive7/BrowserAI/docs/probes/2026-09-24-q261
SERVER="C:/Source/SixFive7/BrowserAI/src/BrowserAI/bin/Release/net10.0-windows/win-x64/publish/BrowserAI.Server.exe"
SCRATCH_ROOT="C:/Users/jori/Downloads/tmp-q261-clients/$NAME"

CODEX_BIN=$(ls -d /c/Users/jori/AppData/Local/OpenAI/Codex/bin/*/codex.exe 2>/dev/null | head -1)

if [ -z "${CODEX_BIN:-}" ]; then
    echo "no codex.exe under AppData\\Local\\OpenAI\\Codex\\bin -- nothing to drive"
    exit 3
fi

mkdir -p "$W/logs" "$W/out" "$W/proj" "$W/codexhome" "$W/sessions"
rm -f "$W/logs/$NAME."* "$W/out/$NAME."*

cat > "$W/codexhome/config.toml" <<CFG
model = "stub-model"
model_provider = "stub"
approval_policy = "never"
sandbox_mode = "read-only"

[model_providers.stub]
name = "stub"
base_url = "http://127.0.0.1:8999"
wire_api = "responses"
env_key = "STUB_KEY"

[mcp_servers.browserai]
command = "C:/Program Files/nodejs/node.exe"
args = ["$RIG/shim.js"]
env = { Q261_SERVER = "$SERVER", Q261_LOGDIR = "$F/logs", Q261_TAG = "$NAME", Q261_DIE_AFTER_CALLS = "0", BROWSERAI_ROOT = "$SCRATCH_ROOT" }
startup_timeout_sec = 30
tool_timeout_sec = 60
CFG

cat > "$W/logs/$NAME.steps.json" <<STEPS
[
 {"m":"initialize","p":{"clientInfo":{"name":"probe-driver","title":"probe","version":"1.0.0"}}},
 {"m":"thread/start","p":{}},
 {"m":"mcpServer/tool/call","p":{"threadId":"\$THREAD","server":"browserai","tool":"browserai_list","arguments":{"directory":"$F/sessions"}}},
 {"sleep":1000},
 {"marker":"a second call on the same thread"},
 {"m":"mcpServer/tool/call","p":{"threadId":"\$THREAD","server":"browserai","tool":"browserai_list","arguments":{"directory":"$F/sessions"}}}
]
STEPS

export CODEX_EXE="$(cygpath -m "$CODEX_BIN")"
export CODEX_HOME="$F/codexhome"
export STUB_KEY=not-a-real-key
export DRIVER_LOG="$F/logs/$NAME.driver.log"
export RUST_LOG=codex_core::mcp=trace,codex_rmcp_client=trace,info

"$NODE" "$RIG/appserver.js" "$(cat "$W/logs/$NAME.steps.json")" > "$W/logs/$NAME.driver.out" 2>&1

echo "--- shim launches ---"
cat "$W/logs/$NAME.launches.log" 2>/dev/null
echo "--- the order of the first frames, which is the whole measurement ---"
"$NODE" -e '
const fs = require("fs");
for (const line of fs.readFileSync(process.argv[1], "utf8").split("\n").filter(Boolean)) {
  const d = JSON.parse(line);
  if (d.frame && typeof d.frame === "object") {
    console.log(d.at.slice(11, 23), d.shim, d.direction.padEnd(14), "id=" + String(d.frame.id).padEnd(5), d.frame.method || "(response)", d.frame.name || "");
  }
}
' "$W/logs/$NAME.wire.jsonl"
