# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# Claude Code against the handover or relay shape.
# usage: cc-ho.sh <run-name> <server-js-abs> <stub-script> <swap-before-turn> <port> [extra env KEY=VAL ...]
set -u
WU=/c/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
F=C:/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
NAME="$1"; SERVERJS="$2"; SCRIPT="$3"; SWAPBEFORE="$4"; PORT="$5"; shift 5
NODE="C:/Program Files/nodejs/node.exe"
mkdir -p "$WU/logs" "$WU/out" "$WU/proj"
rm -f "$WU/logs/$NAME."* "$WU/out/$NAME."*
printf '1.0.0' > "$WU/ho-install/VERSION"

EXTRA_ENV=""
for kv in "$@"; do EXTRA_ENV="$EXTRA_ENV,\"${kv%%=*}\":\"${kv#*=}\""; done

"$NODE" -e "
const fs=require('fs');
const name=process.argv[1], serverjs=process.argv[2], w=process.argv[3], extra=process.argv[4];
const env={HO_LOGDIR:w+'/logs',HO_TAG:name,HO_INSTALL_DIR:w+'/ho-install',HO_HELPER_DIR:w+'/ho-helper',
  HO_STATE:w+'/logs/'+name+'.state.json',HO_APPLY_DONE:w+'/logs/'+name+'.apply.done',
  HO_SWAP_REQUEST:w+'/logs/'+name+'.swap.request',HO_SWAP_DONE:w+'/logs/'+name+'.swap.done',HO_TRIGGER:'after-first-call'};
const more=JSON.parse('{'+extra.replace(/^,/,'')+'}');
Object.assign(env,more);
const cfg={mcpServers:{probe:{type:'stdio',command:'C:/Program Files/nodejs/node.exe',args:[serverjs],env}}};
fs.writeFileSync(w+'/rig/mcp-'+name+'.json',JSON.stringify(cfg,null,1));
" "$NAME" "$SERVERJS" "$F" "$EXTRA_ENV"

export STUB_PORT="$PORT" STUB_LOGDIR="$F/logs" STUB_TAG="$NAME" STUB_SCRIPT="$SCRIPT"
export STUB_WAITFILE="" STUB_WAITBEFORE=-1
export STUB_SWAP_BEFORE="$SWAPBEFORE"
export STUB_SWAP_REQUEST="$F/logs/$NAME.swap.request"
export STUB_APPLY_DONE="$F/logs/$NAME.apply.done"
export STUB_SWAP_DONE="$F/logs/$NAME.swap.done"
export STUB_VERSION_FILE="$F/ho-install/VERSION"
export STUB_SWAP_WAIT_MS=5000
"$NODE" "$F/rig/apistub.js" > "$WU/logs/$NAME.stub.out" 2>&1 &
SP=$!
sleep 1

export CLAUDE_CONFIG_DIR="$F/cfg"
export ANTHROPIC_BASE_URL="http://127.0.0.1:$PORT"
export ANTHROPIC_API_KEY="stub-key-not-real"
export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
unset CLAUDE_CODE_ENTRYPOINT CLAUDECODE CLAUDE_CODE_SESSION_ID CLAUDE_CODE_CHILD_SESSION
cd "$WU/proj" || exit 9
START=$(date +%s)
claude -p \
  --mcp-config "$F/rig/mcp-$NAME.json" \
  --strict-mcp-config \
  --model sonnet \
  --tools "" \
  --allowedTools "mcp__probe__ping" \
  --permission-mode acceptEdits \
  --output-format stream-json --verbose \
  --debug-file "$F/out/$NAME.debug.log" \
  "Call the probe ping tool as instructed." \
  > "$WU/out/$NAME.stream.jsonl" 2> "$WU/out/$NAME.stderr.txt" < /dev/null
RC=$?
END=$(date +%s)
kill $SP 2>/dev/null
echo "--- $NAME claude_exit=$RC elapsed=$((END-START))s"
