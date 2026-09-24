# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# usage: probe.sh <run-name> <server-mode> <stub-script> <waitbefore> <port> [extra claude args...]
set -u
WU=/c/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
F=C:/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
NAME="$1"; SMODE="$2"; SCRIPT="$3"; WAITBEFORE="$4"; PORT="$5"; shift 5
NODE="C:/Program Files/nodejs/node.exe"
mkdir -p "$WU/logs" "$WU/out" "$WU/proj"
rm -f "$WU/logs/$NAME."* "$WU/out/$NAME."*

"$NODE" -e "const fs=require('fs');const n=process.argv[1],m=process.argv[2],w=process.argv[3];const cfg={mcpServers:{probe:{type:'stdio',command:'C:/Program Files/nodejs/node.exe',args:[w+'/rig/server.js'],env:{PROBE_LOGDIR:w+'/logs',PROBE_TAG:n,PROBE_MODE:m}}}};fs.writeFileSync(w+'/rig/mcp-'+n+'.json',JSON.stringify(cfg));" "$NAME" "$SMODE" "$F"

export STUB_PORT="$PORT" STUB_LOGDIR="$F/logs" STUB_TAG="$NAME" STUB_SCRIPT="$SCRIPT"
export STUB_WAITFILE="$F/logs/$NAME.exited" STUB_WAITBEFORE="$WAITBEFORE"
"$NODE" "$F/rig/apistub.js" > "$WU/logs/$NAME.stub.out" 2>&1 &
STUBPID=$!
sleep 1

export CLAUDE_CONFIG_DIR="$F/cfg"
export ANTHROPIC_BASE_URL="http://127.0.0.1:$PORT"
export ANTHROPIC_API_KEY="stub-key-not-real"
export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
unset CLAUDE_CODE_ENTRYPOINT CLAUDECODE CLAUDE_CODE_SESSION_ID CLAUDE_CODE_CHILD_SESSION
cd "$WU/proj" || exit 9
echo "=== $(date -Is) START $NAME mode=$SMODE waitbefore=$WAITBEFORE ===" >> "$WU/out/$NAME.meta"
claude -p \
  --mcp-config "$F/rig/mcp-$NAME.json" \
  --strict-mcp-config \
  --model sonnet \
  --tools "" \
  --allowedTools "mcp__probe__ping" \
  --permission-mode acceptEdits \
  --output-format stream-json --verbose \
  --debug-file "$F/out/$NAME.debug.log" \
  "$@" \
  "Call the probe ping tool as instructed." \
  > "$WU/out/$NAME.stream.jsonl" 2> "$WU/out/$NAME.stderr.txt" < /dev/null
RC=$?
echo "claude_exit=$RC" >> "$WU/out/$NAME.meta"
kill $STUBPID 2>/dev/null
echo "=== $(date -Is) END $NAME ===" >> "$WU/out/$NAME.meta"
echo "RC=$RC"
