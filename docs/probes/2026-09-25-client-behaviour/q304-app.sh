#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Q304 a: one `codex app-server` run in the trusted scratch project.
#   q304-app.sh <run> before   started BEFORE the install with the environment a process
#                              started then gets; its post-install steps wait for
#                              q304/installed.flag, so it is still running across the install.
#   q304-app.sh <run> after    started AFTER the install with the environment a process
#                              started now gets (CreateEnvironmentBlock, read at this moment).
set -u
R="${1:?run}"; KIND="${2:?before|after}"
W=/c/Source/SixFive7/BrowserAI/.work/client-behaviour
F=C:/Source/SixFive7/BrowserAI/.work/client-behaviour
Q="$W/q304"; QF="$F/q304"
NODE="C:/Program Files/nodejs/node.exe"
CODEX="%USERPROFILE%/AppData/Local/OpenAI/Codex/bin/247581e40ee272fb/codex.exe"
SERVER_W='%USERPROFILE%\Downloads\tmp-client-behaviour\q304\root\current\BrowserAI.Server.exe'
PROJ_W="$(cygpath -w "$Q/proj")"
RUN="$Q/runs/$R"; RUNF="$QF/runs/$R"
rm -f "$RUN"/driver.log* "$RUN"/app-env.json "$RUN"/steps.json

if [ "$KIND" = "after" ]; then
  pwsh -NoProfile -NonInteractive -File "$F/rig/q304-helpers.ps1" -Do fresh-env -Out "$RUNF/fresh-env.json" > /dev/null
  BASE="$RUNF/fresh-env.json"
else
  BASE="$QF/readings/1-before.fresh-env.json"
fi

"$NODE" -e '
const fs=require("fs"); const [base,out,home]=process.argv.slice(1);
const e=JSON.parse(fs.readFileSync(base,"utf8"));
delete e.Q304_DROPPED_VARIABLE_NAMES;
Object.assign(e,{CODEX_HOME:home, Q304_STUB_KEY:"not-a-real-key", RUST_LOG:"codex_rmcp_client=debug,codex_mcp=debug,warn"});
fs.writeFileSync(out, JSON.stringify(e,null,1));
const p=(e.Path||"").split(";");
console.log(`environment from ${base}: PATH has ${p.length} entries; naming BrowserAI: ${JSON.stringify(p.filter(x=>/BrowserAI/i.test(x)))}`);
' "$BASE" "$RUNF/app-env.json" "$(cygpath -w "$RUN/home")" | tee "$RUN/env-summary.txt"

"$NODE" -e '
const fs=require("fs"); const [out,kind,proj,server,flag]=process.argv.slice(1);
const call={m:"mcpServer/tool/call",p:{threadId:"$THREAD",server:"browserai",tool:"browserai_list",arguments:{directory:proj}},timeout:60000};
const steps=[
 {m:"initialize",p:{clientInfo:{name:"q304-probe",title:"q304",version:"1"}}},
 {marker: kind==="before" ? "THREAD 1, started BEFORE the install" : "THREAD 1, app-server started AFTER the install"},
 {m:"thread/start",p:{cwd:proj}},
 {waitStatus:"browserai",timeout:30000},
 {procs:server}];
if (kind==="after") steps.push({marker:"CALL on thread 1"}, call, {procs:server});
if (kind==="before") steps.push(
 {hold:flag},
 {marker:"INSTALLED: the same app-server, still running"},
 {marker:"CALL on thread 1 (its server failed before the install)"}, call,
 {marker:"THREAD 2, a new thread in the same app-server"},
 {m:"thread/start",p:{cwd:proj}},
 {waitStatus:"browserai",timeout:30000},
 {procs:server},
 {marker:"CALL on thread 2"}, call,
 {marker:"RELOAD: config/mcpServer/reload"},
 {m:"config/mcpServer/reload",p:null},
 {sleep:4000},
 {procs:server},
 {marker:"CALL on thread 2 after the reload"}, call,
 {procs:server});
fs.writeFileSync(out, JSON.stringify(steps,null,1));
' "$RUNF/steps.json" "$KIND" "$PROJ_W" "$SERVER_W" "$QF/installed.flag"

export CODEX_EXE="$CODEX" DRIVER_LOG="$RUNF/driver.log" DRIVER_CWD="$PROJ_W" APP_ENV_FILE="$RUNF/app-env.json"
echo "$(date -u +%FT%T.%3NZ) $R ($KIND) driver start" >> "$Q/logs/timeline.txt"
timeout 900 "$NODE" "$F/rig/appdrv.js" "$RUNF/steps.json" > "$RUN/driver.out" 2>&1
echo "$(date -u +%FT%T.%3NZ) $R ($KIND) driver exit=$?" >> "$Q/logs/timeline.txt"
