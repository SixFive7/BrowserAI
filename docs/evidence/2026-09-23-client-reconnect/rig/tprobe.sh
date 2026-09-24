# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# usage: tprobe.sh <run-name> <server-mode> <steps-json-file> <stub-script>
set -u
WU=/c/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
F=C:/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
NAME="$1"; SMODE="$2"; STEPS="$3"; SCRIPT="$4"
NODE="C:/Program Files/nodejs/node.exe"
export CODEX_EXE="C:/Users/jori/AppData/Local/OpenAI/Codex/bin/247581e40ee272fb/codex.exe"
export CODEX_HOME="$F/codexhome"
export STUB_KEY=not-a-real-key
export DRIVER_LOG="$F/logs/$NAME.driver.log"
rm -f "$WU/logs/$NAME."*
cat > "$WU/codexhome/config.toml" <<CFG
model = "stub-model"
model_provider = "stub"
approval_policy = "never"
sandbox_mode = "danger-full-access"

[model_providers.stub]
name = "stub"
base_url = "http://127.0.0.1:8910"
wire_api = "responses"
env_key = "STUB_KEY"

[mcp_servers.probe]
command = "C:/Program Files/nodejs/node.exe"
args = ["$F/rig/server.js"]
env = { PROBE_LOGDIR = "$F/logs", PROBE_TAG = "$NAME", PROBE_MODE = "$SMODE" }
startup_timeout_sec = 20
tool_timeout_sec = 60
CFG
export STUB_PORT=8910 STUB_LOGDIR="$F/logs" STUB_TAG="$NAME-stub" STUB_SCRIPT="$SCRIPT"
export STUB_WAITFILE="" STUB_WAITBEFORE=-1
"$NODE" "$F/rig/openaistub.js" > "$WU/logs/$NAME.stub.out" 2>&1 &
SP=$!
sleep 1
"$NODE" "$F/rig/appserver.js" "$(cat "$STEPS")" > "$WU/logs/$NAME.driver.out" 2>&1
kill $SP 2>/dev/null
echo "--- $NAME launches: $(grep -c 'LAUNCH mode' "$WU/logs/$NAME.launches.log" 2>/dev/null || echo 0)"
grep -E "MARKER|NOTE .*[Mm]cpToolCall|TIMEOUT" "$WU/logs/$NAME.driver.log" | sed -E 's/^[0-9T:.Z-]+ //' | cut -c1-260
