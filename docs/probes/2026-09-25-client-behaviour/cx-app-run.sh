#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# One `codex app-server` run for Q296 a.
#
# usage: cx-app-run.sh <run-name> <port> <server: stub|real> [stub-mode]
#
# The "updater" is a file for the stub (US_UPDATER_FILE) and a stand-in Update.exe for the
# real server; the driver removes the file / the run script ends the stand-in at the step
# marked UPDATER EXITS. Nothing here touches the maintainer's own state: CODEX_HOME is a
# scratch directory, the model provider is a local stub.
set -u
NAME="${1:?run name}"; PORT="${2:?port}"; SERVER="${3:?stub|real}"; SMODE="${4:-updater-file}"
export SMODE
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
UPDATER="$RF/logs/updater.running"

if [ "$SERVER" = "stub" ]; then
  SERVERTOML="command = \"C:/Program Files/nodejs/node.exe\"
args = [\"$F/rig/updstub.js\"]
env = { US_LOGDIR = \"$RF/logs\", US_TAG = \"$NAME\", US_MODE = \"$SMODE\", US_UPDATER_FILE = \"$UPDATER\" }"
else
  SERVERTOML="command = \"C:/Program Files/nodejs/node.exe\"
args = [\"$F/rig/passthru.js\"]
env = { PT_SERVER = \"${REAL_SERVER:?REAL_SERVER}\", PT_LOGDIR = \"$RF/logs\", PT_TAG = \"$NAME\", BROWSERAI_ROOT = \"${REAL_ROOT:?REAL_ROOT}\" }"
fi

cat > "$RUN/home/config.toml" <<CFG
model = "stub-model"
model_provider = "stub"
approval_policy = "never"
sandbox_mode = "danger-full-access"

[model_providers.stub]
name = "stub"
base_url = "http://127.0.0.1:$PORT"
wire_api = "responses"
env_key = "STUB_KEY"

[mcp_servers.browserai]
$SERVERTOML
$( [ "${STARTUP_TIMEOUT_SEC:-20}" = "default" ] || echo "startup_timeout_sec = ${STARTUP_TIMEOUT_SEC:-20}" )
tool_timeout_sec = 60
CFG

# The model's moves: one call and one closing text per turn, three turns.
"$NODE" -e '
const fs=require("fs"); const [out,dir]=process.argv.slice(1);
const call={tool:"mcp__browserai/browserai_list",args:{directory:dir}};
fs.writeFileSync(out, JSON.stringify([call,{text:"turn one done"},call,{text:"turn two done"},call,{text:"turn three done"},call,{text:"new thread done"}]));
' "$RUN/model.json" "$DIR"

"$NODE" -e '
const fs=require("fs"); const [out,updater,proj,dir,logs,tag,real]=process.argv.slice(1);
const call={m:"mcpServer/tool/call",p:{threadId:"$THREAD",server:"browserai",tool:"browserai_list",arguments:{directory:dir}},timeout:60000};
const turn=(text)=>({m:"turn/start",p:{threadId:"$THREAD",input:[{type:"text",text}]},timeout:60000,waitNote:"turn/completed",waitNoteTimeout:90000});
const live={checkLaunches:{dir:logs,tag}};
const steps=[];
const held = process.env.SMODE === "held-updater-file";
if (real !== "real") steps.push({touch:updater});
steps.push({m:"initialize",p:{clientInfo:{name:"q296-probe",title:"q296",version:"1"}}});
if (held) steps.push({rmAfter:updater, ms:Number(process.env.HOLD_MS||3000)});
steps.push(
 {m:"thread/start",p:{cwd:proj}},
 {waitStatus:"browserai",timeout:30000},
 {sleep:500}, live,
 {marker:"A: mcpServer/tool/call on the thread, updater still running"}, call,
 {marker:"T1: a model turn, updater still running"}, turn("List the BrowserAI sessions under the directory."),
 live,
 {marker:"UPDATER EXITS"});
if (real === "real") steps.push({touch: logs + "/end-the-updater"}, {waitFile: logs + "/updater-ended", timeout: 30000});
else if (!held) steps.push({rm:updater});
steps.push(
 {sleep:2500}, live,
 {marker:"B: mcpServer/tool/call on the same thread after the updater exited"}, call,
 {marker:"T2: a model turn on the same thread after the updater exited"}, turn("List them again."),
 live,
 {marker:"RELOAD: config/mcpServer/reload"}, {m:"config/mcpServer/reload",p:null},
 {sleep:3000}, live,
 {marker:"C: mcpServer/tool/call after the reload"}, call,
 {marker:"T3: a model turn after the reload"}, turn("And once more."),
 {marker:"NEW THREAD"}, {m:"thread/start",p:{cwd:proj}}, {waitStatus:"browserai",timeout:30000},
 {marker:"D: mcpServer/tool/call on the new thread"}, call,
 {marker:"T4: a model turn on the new thread"}, turn("On the new thread."),
 live);
fs.writeFileSync(out, JSON.stringify(steps,null,1));
' "$RUN/steps.json" "$UPDATER" "$RF/proj" "$DIR" "$RF/logs" "$NAME" "$SERVER"

export STUB_PORT="$PORT" STUB_LOGDIR="$RF/logs" STUB_TAG="$NAME-model" STUB_SCRIPT_FILE="$RF/model.json"
"$NODE" "$F/rig/cxstub.js" > "$RUN/logs/$NAME-model.stub.out" 2>&1 &
SP=$!
sleep 1

export CODEX_EXE="$CODEX" CODEX_HOME="$RF/home" STUB_KEY=not-a-real-key DRIVER_LOG="$RF/logs/$NAME.driver.log" DRIVER_CWD="$RF/proj"
export RUST_LOG=codex_core::mcp=trace,codex_rmcp_client=trace,codex_mcp=trace,info
unset CLAUDECODE CLAUDE_CODE_ENTRYPOINT CLAUDE_CODE_SSE_PORT
echo "$(date -u +%FT%T.%3NZ) driver start" >> "$RUN/out/meta.txt"
timeout 600 "$NODE" "$F/rig/appdrv.js" "$RF/steps.json" > "$RUN/logs/$NAME.driver.out" 2>&1
echo "$(date -u +%FT%T.%3NZ) driver exit=$?" >> "$RUN/out/meta.txt"
kill $SP 2>/dev/null; wait $SP 2>/dev/null
echo "--- launches ---"; cat "$RUN/logs/$NAME.launches.log" 2>/dev/null
