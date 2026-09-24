# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# usage: dprobe.sh <run-name> <server-mode> <steps-json-file>
set -u
WU=/c/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
F=C:/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
NAME="$1"; SMODE="$2"; STEPS="$3"
export CODEX_EXE="C:/Users/jori/AppData/Local/OpenAI/Codex/bin/247581e40ee272fb/codex.exe"
export CODEX_HOME="$F/codexhome"
export STUB_KEY=not-a-real-key
export DRIVER_LOG="$F/logs/$NAME.driver.log"
rm -f "$WU/logs/$NAME."*
cat > "$WU/codexhome/config.toml" <<CFG
model = "stub-model"
model_provider = "stub"
approval_policy = "never"
sandbox_mode = "read-only"

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
"C:/Program Files/nodejs/node.exe" "$F/rig/appserver.js" "$(cat "$STEPS")" > "$WU/logs/$NAME.driver.out" 2>&1
echo "--- $NAME launches: $(grep -c 'LAUNCH mode' "$WU/logs/$NAME.launches.log" 2>/dev/null || echo 0)"
grep -E "MARKER|RESP id=[3-9]|TIMEOUT" "$WU/logs/$NAME.driver.log" | sed -E 's/^[0-9T:.Z-]+ //' | cut -c1-200
