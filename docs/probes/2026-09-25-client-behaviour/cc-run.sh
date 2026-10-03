#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# One Claude Code run for Q296 a: a first headless session whose BrowserAI starts in
# updating mode, then a `claude -p --continue` in a new process.
#
# usage: cc-run.sh <run-name> <port> <server: stub|real> [stub-mode]
#
# Nothing here touches the maintainer's own state:
#   CLAUDE_CONFIG_DIR   a scratch directory seeded as onboarded (OnboardedClientConfig's recipe)
#   ANTHROPIC_BASE_URL  a local stub; no credential, no inference
#   BROWSERAI_ROOT      (real server only) a scratch app root under the profile
set -u
NAME="${1:?run name}"; PORT="${2:?port}"; SERVER="${3:?stub|real}"; SMODE="${4:-updating-first}"

W=/c/Source/SixFive7/BrowserAI/.work/client-behaviour
F=C:/Source/SixFive7/BrowserAI/.work/client-behaviour
NODE="C:/Program Files/nodejs/node.exe"
RUN="$W/runs/$NAME"; RF="$F/runs/$NAME"
rm -rf "$RUN"; mkdir -p "$RUN/cfg" "$RUN/proj" "$RUN/logs" "$RUN/out"

if [ "$SERVER" = "real" ]; then
  # The stand-in updater under the scratch app root, started before the client can start
  # the server, and ended by its own handle (standin.js) after STANDIN_LIFE_MS or on the
  # trigger file <logs>/end-the-updater.
  mkdir -p "${REAL_ROOT:?REAL_ROOT}"
  SI_ROOT="$REAL_ROOT" SI_LOGS="$RF/logs" SI_LIFE_MS="${STANDIN_LIFE_MS:-600000}" "$NODE" "$F/rig/standin.js" > "$RUN/logs/standin.out" 2>&1 &
  for i in $(seq 1 100); do [ -f "$RUN/logs/updater.pid" ] && break; sleep 0.1; done
  echo "$(date -u +%FT%T.%3NZ) stand-in updater pid=$(cat "$RUN/logs/updater.pid" 2>/dev/null)" >> "$RUN/out/meta.txt"
fi

printf '%s' '{"hasCompletedOnboarding":true,"autoUpdates":false,"bypassPermissionsModeAccepted":false}' > "$RUN/cfg/.claude.json"

DIR="$RF/sessions"
mkdir -p "$RUN/sessions"

if [ "$SERVER" = "stub" ]; then
  "$NODE" -e '
const fs=require("fs"); const [out,rig,logs,tag,mode]=process.argv.slice(1);
fs.writeFileSync(out, JSON.stringify({mcpServers:{browserai:{type:"stdio",command:"C:/Program Files/nodejs/node.exe",args:[rig+"/updstub.js"],env:{US_LOGDIR:logs,US_TAG:tag,US_MODE:mode,US_EXIT_AFTER_MS:process.env.US_EXIT_AFTER_MS||"4000",US_UPDATER_FILE:logs+"/updater.running",US_LIST_CHANGED_CAP:process.env.US_LIST_CHANGED_CAP||"0"}}}}));
' "$RUN/mcp.json" "$F/rig" "$RF/logs" "$NAME" "$SMODE"
  EXITED="${S1_T1_WAITFILE:-$RF/logs/$NAME.exited.1}"
else
  # The real published server behind a full-frame pass-through, in real updating mode:
  # a stand-in Update.exe (a copy of cmd.exe) runs under the scratch app root, and the
  # run script ends it after UPDATER_LIFE_MS.
  ROOT="${REAL_ROOT:?REAL_ROOT}"
  "$NODE" -e '
const fs=require("fs"); const [out,rig,logs,tag,server,root]=process.argv.slice(1);
fs.writeFileSync(out, JSON.stringify({mcpServers:{browserai:{type:"stdio",command:"C:/Program Files/nodejs/node.exe",args:[rig+"/passthru.js"],env:{PT_SERVER:server,PT_LOGDIR:logs,PT_TAG:tag,BROWSERAI_ROOT:root}}}}));
' "$RUN/mcp.json" "$F/rig" "$RF/logs" "$NAME" "${REAL_SERVER:?REAL_SERVER}" "$ROOT"
  EXITED="$RF/logs/$NAME.exited.1"
fi

# Session 1: turn 0 calls the tool while the first server lives; turn 1 waits for
# that server to have ended its conversation, then calls again; turn 2 waits 5 s
# more and calls again; turn 3 ends the turn with text.
"$NODE" -e '
const fs=require("fs"); const [out,exited,dir]=process.argv.slice(1);
const call={tool:"mcp__browserai__browserai_list",args:{directory:dir}};
fs.writeFileSync(out, JSON.stringify([
  call,
  Object.assign({waitFile:exited,waitMaxMs:60000,delayMs:1500},call),
  Object.assign({delayMs:Number(process.env.S1_T2_DELAY_MS||5000)},call),
  {text:"session one is done"}
]));
' "$RUN/script1.json" "$EXITED" "$DIR"

# Session 2 (`--continue`, a new process): one call, then text.
"$NODE" -e '
const fs=require("fs"); const [out,dir]=process.argv.slice(1);
fs.writeFileSync(out, JSON.stringify([
  {tool:"mcp__browserai__browserai_list",args:{directory:dir}},
  {text:"session two is done"}
]));
' "$RUN/script2.json" "$DIR"

export CLAUDE_CONFIG_DIR="$RF/cfg"
export ANTHROPIC_BASE_URL="http://127.0.0.1:$PORT"
export ANTHROPIC_API_KEY="stub-key-not-real"
export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
unset CLAUDE_CODE_ENTRYPOINT CLAUDECODE CLAUDE_CODE_SESSION_ID CLAUDE_CODE_CHILD_SESSION CLAUDE_CODE_SSE_PORT

cd "$RUN/proj" || exit 9

if [ "$SERVER" = "stub" ] && [[ "$SMODE" == *updater-file* ]]; then
  : > "$RUN/logs/updater.running"
  ( sleep "$(awk "BEGIN{print ${UPDATER_FILE_LIFE_MS:-4000}/1000}")"; rm -f "$RUN/logs/updater.running"; echo "$(date -u +%FT%T.%3NZ) updater file removed" >> "$RUN/out/meta.txt" ) &
fi
for S in 1 2; do
  if [ "$S" = "2" ] && [ "$SERVER" = "real" ]; then
    for i in $(seq 1 600); do [ -f "$RUN/logs/updater-ended" ] && break; sleep 0.1; done
    echo "$(date -u +%FT%T.%3NZ) updater ended: $(cat "$RUN/logs/updater-ended" 2>/dev/null)" >> "$RUN/out/meta.txt"
  fi
  export STUB_PORT="$PORT" STUB_LOGDIR="$RF/logs" STUB_TAG="$NAME-s$S" STUB_SCRIPT_FILE="$RF/script$S.json"
  "$NODE" "$F/rig/ccstub.js" > "$RUN/logs/$NAME-s$S.stub.out" 2>&1 &
  STUB=$!
  sleep 1
  CONT=""; [ "$S" = "2" ] && CONT="--continue"
  echo "$(date -u +%FT%T.%3NZ) session $S start" >> "$RUN/out/meta.txt"
  timeout 240 claude -p $CONT --mcp-config "$RF/mcp.json" --strict-mcp-config --model sonnet \
      --tools "" --allowedTools "mcp__browserai__browserai_list" --permission-mode acceptEdits \
      --output-format stream-json --verbose --debug-file "$RF/out/s$S.debug.log" \
      "List the BrowserAI sessions under the directory." \
      > "$RUN/out/s$S.stream.jsonl" 2> "$RUN/out/s$S.stderr.txt" < /dev/null
  echo "$(date -u +%FT%T.%3NZ) session $S claude exit=$?" >> "$RUN/out/meta.txt"
  kill $STUB 2>/dev/null; wait $STUB 2>/dev/null
  sleep 1
done
echo "--- launches ---"; cat "$RUN/logs/$NAME.launches.log" 2>/dev/null
