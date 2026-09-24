# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# Same as probe.sh but the server is registered at USER scope with `claude mcp add`,
# exactly as the product does, instead of via --mcp-config.
set -u
WU=/c/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
F=C:/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
NAME="$1"; SMODE="$2"; SCRIPT="$3"; WAITBEFORE="$4"; PORT="$5"
NODE="C:/Program Files/nodejs/node.exe"
mkdir -p "$WU/logs" "$WU/out" "$WU/proj" "$WU/cfg-user"
rm -f "$WU/logs/$NAME."* "$WU/out/$NAME."*
# fresh scratch config dir, seeded onboarded per OnboardedClientConfig
rm -rf "$WU/cfg-user"; mkdir -p "$WU/cfg-user"
printf '%s' '{"hasCompletedOnboarding":true,"autoUpdates":false,"bypassPermissionsModeAccepted":false}' > "$WU/cfg-user/.claude.json"
export CLAUDE_CONFIG_DIR="$F/cfg-user"
cd "$WU/proj" || exit 9
claude mcp add probe --scope user --env "PROBE_LOGDIR=$F/logs" --env "PROBE_TAG=$NAME" --env "PROBE_MODE=$SMODE" -- "C:/Program Files/nodejs/node.exe" "$F/rig/server.js" > "$WU/out/$NAME.add.txt" 2>&1
echo "mcp add exit=$? :: $(cat "$WU/out/$NAME.add.txt")"

export STUB_PORT="$PORT" STUB_LOGDIR="$F/logs" STUB_TAG="$NAME" STUB_SCRIPT="$SCRIPT"
export STUB_WAITFILE="$F/logs/$NAME.exited" STUB_WAITBEFORE="$WAITBEFORE"
"$NODE" "$F/rig/apistub.js" > "$WU/logs/$NAME.stub.out" 2>&1 &
SP=$!
sleep 1
export ANTHROPIC_BASE_URL="http://127.0.0.1:$PORT"
export ANTHROPIC_API_KEY="stub-key-not-real"
export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
unset CLAUDE_CODE_ENTRYPOINT CLAUDECODE CLAUDE_CODE_SESSION_ID CLAUDE_CODE_CHILD_SESSION
claude -p --model sonnet --tools "" --allowedTools "mcp__probe__ping" \
  --permission-mode acceptEdits --output-format stream-json --verbose \
  --debug-file "$F/out/$NAME.debug.log" "Call the probe ping tool as instructed." \
  > "$WU/out/$NAME.stream.jsonl" 2> "$WU/out/$NAME.stderr.txt" < /dev/null
echo "claude_exit=$?"
kill $SP 2>/dev/null
