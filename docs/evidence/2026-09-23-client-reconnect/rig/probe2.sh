# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# usage: probe2.sh <run-name> <server-mode> <port> <phase2-extra-args>
set -u
WU=/c/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
F=C:/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
NAME="$1"; SMODE="$2"; PORT="$3"; P2EXTRA="$4"
NODE="C:/Program Files/nodejs/node.exe"
mkdir -p "$WU/logs" "$WU/out" "$WU/proj"
rm -f "$WU/logs/$NAME."* "$WU/out/$NAME."*
"$NODE" -e "const fs=require('fs');const n=process.argv[1],m=process.argv[2],w=process.argv[3];const cfg={mcpServers:{probe:{type:'stdio',command:'C:/Program Files/nodejs/node.exe',args:[w+'/rig/server.js'],env:{PROBE_LOGDIR:w+'/logs',PROBE_TAG:n,PROBE_MODE:m}}}};fs.writeFileSync(w+'/rig/mcp-'+n+'.json',JSON.stringify(cfg));" "$NAME" "$SMODE" "$F"

export CLAUDE_CONFIG_DIR="$F/cfg"
export ANTHROPIC_BASE_URL="http://127.0.0.1:$PORT"
export ANTHROPIC_API_KEY="stub-key-not-real"
export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
unset CLAUDE_CODE_ENTRYPOINT CLAUDECODE CLAUDE_CODE_SESSION_ID CLAUDE_CODE_CHILD_SESSION

run_phase () {
  local PHASE="$1"; local SCRIPT="$2"; shift 2
  export STUB_PORT="$PORT" STUB_LOGDIR="$F/logs" STUB_TAG="$NAME-$PHASE" STUB_SCRIPT="$SCRIPT"
  export STUB_WAITFILE="$F/logs/$NAME.exited" STUB_WAITBEFORE="-1"
  "$NODE" "$F/rig/apistub.js" > "$WU/logs/$NAME-$PHASE.stub.out" 2>&1 &
  local SP=$!
  sleep 1
  cd "$WU/proj" || exit 9
  claude -p --mcp-config "$F/rig/mcp-$NAME.json" --strict-mcp-config --model sonnet \
    --tools "" --allowedTools "mcp__probe__ping" --permission-mode acceptEdits \
    --output-format stream-json --verbose --debug-file "$F/out/$NAME-$PHASE.debug.log" \
    "$@" "Call the probe ping tool." \
    > "$WU/out/$NAME-$PHASE.stream.jsonl" 2> "$WU/out/$NAME-$PHASE.stderr.txt" < /dev/null
  echo "phase $PHASE exit=$?" >> "$WU/out/$NAME.meta"
  kill $SP 2>/dev/null
  sleep 1
}

run_phase p1 'tool:mcp__probe__ping:{"note":"phase1"}||text:phase1 done'
run_phase p2 'tool:mcp__probe__ping:{"note":"phase2"}||text:phase2 done' $P2EXTRA
cat "$WU/out/$NAME.meta"
