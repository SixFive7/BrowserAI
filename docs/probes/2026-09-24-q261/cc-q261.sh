# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# Drives the REAL Claude Code against the REAL published BrowserAI, with the
# server made to exit between two tool calls.
#
# usage: cc-q261.sh <run-name> <port> [hold-seconds-before-the-retry]
#
# Nothing here touches the maintainer's own state:
#   CLAUDE_CONFIG_DIR   a scratch directory, seeded as onboarded
#   BROWSERAI_ROOT      a scratch app root, so the machine-wide browsers root,
#                       session index, live markers and process log are this
#                       run's own and no sweep can reach another agent's browser
#   ANTHROPIC_BASE_URL  a local stub, so no credential is used and no inference
#                       happens anywhere
set -u

NAME="${1:?run name}"
PORT="${2:?port}"

REPO=/c/Source/SixFive7/BrowserAI
W=$REPO/.work/q261-2026-09-24
F=C:/Source/SixFive7/BrowserAI/.work/q261-2026-09-24
NODE="C:/Program Files/nodejs/node.exe"
RIG=C:/Source/SixFive7/BrowserAI/docs/probes/2026-09-24-q261
SERVER="C:/Source/SixFive7/BrowserAI/src/BrowserAI/bin/Release/net10.0-windows/win-x64/publish/BrowserAI.Server.exe"

# The scratch app root must be under the user's profile: InstallRootScope refuses
# anything else, so the repository's own .work cannot be used for this one thing.
SCRATCH_ROOT="C:/Users/jori/Downloads/tmp-q261-clients/$NAME"

mkdir -p "$W/logs" "$W/out" "$W/proj" "$W/cfg" "$W/sessions"
rm -rf "$W/logs/$NAME."* "$W/out/$NAME."*

# The client's own recipe for a throwaway configuration directory, which is what
# tests/BrowserAI.Tests/Harness/OnboardedClientConfig.cs writes.
printf '%s' '{"hasCompletedOnboarding":true,"autoUpdates":false,"bypassPermissionsModeAccepted":false}' > "$W/cfg/.claude.json"

# BrowserAI behind the shim, registered the way --mcp-config registers anything.
"$NODE" -e '
const fs = require("fs");
const [name, rig, server, root, logs, work] = process.argv.slice(1);
fs.writeFileSync(work + "/mcp-" + name + ".json", JSON.stringify({
  mcpServers: {
    browserai: {
      type: "stdio",
      command: "C:/Program Files/nodejs/node.exe",
      args: [rig + "/shim.js"],
      env: {
        Q261_SERVER: server,
        Q261_LOGDIR: logs,
        Q261_TAG: name,
        Q261_DIE_AFTER_CALLS: "1",
        Q261_DIED_MARKER: logs + "/" + name + ".died-once",
        BROWSERAI_ROOT: root,
      },
    },
  },
}));
' "$NAME" "$RIG" "$SERVER" "$SCRATCH_ROOT" "$F/logs" "$F/logs"

export CLAUDE_CONFIG_DIR="$F/cfg"
export ANTHROPIC_BASE_URL="http://127.0.0.1:$PORT"
export ANTHROPIC_API_KEY="stub-key-not-real"
export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
unset CLAUDE_CODE_ENTRYPOINT CLAUDECODE CLAUDE_CODE_SESSION_ID CLAUDE_CODE_CHILD_SESSION

LIST="{\"directory\":\"$F/sessions\"}"

# Five scripted turns, and the count is what the measurement needs: a call the
# first server answers, a call that meets its closed pipe, the call the re-dialled
# server refuses, the RETRY that refusal asks for, and a sentence to end the turn.
# ⚠️ THE THIRD ARGUMENT IS WHAT DECIDES WHETHER THE NOTIFICATION DID ANYTHING.
# A refusal and a list-changed notification leave on the same pipe in the same
# millisecond, and the retry follows within about forty of them -- far too short a
# window to tell "the client ignored the notification" from "the client had not got
# round to it yet". Pass a number of seconds and the stub holds the turn AFTER the
# refusal for that long, leaving the MCP connection open and idle, so a tools/list
# that is going to arrive has time to.
HOLD="${3:-0}"

if [ "$HOLD" != "0" ]; then
    export STUB_WAITFILE="$F/logs/$NAME.go"
    # Turn 3 is the one that emits the RETRY, so holding before it is what leaves
    # the connection idle between the refusal and the retry -- which is the window
    # a refresh would have to land in to be the thing that fixed anything.
    export STUB_WAITBEFORE="3"
    rm -f "$W/logs/$NAME.go"
    ( sleep "$HOLD"; : > "$W/logs/$NAME.go" ) &
fi

export STUB_PORT="$PORT"
export STUB_LOGDIR="$F/logs"
export STUB_TAG="$NAME"
export STUB_SCRIPT="tool:mcp__browserai__browserai_list:$LIST||tool:mcp__browserai__browserai_list:$LIST||tool:mcp__browserai__browserai_list:$LIST||tool:mcp__browserai__browserai_list:$LIST||text:the four calls are done"

"$NODE" "$RIG/apistub.js" > "$W/logs/$NAME.stub.out" 2>&1 &
STUB=$!
sleep 1

cd "$W/proj" || exit 9

claude -p --mcp-config "$F/logs/mcp-$NAME.json" --strict-mcp-config --model sonnet \
    --tools "" --allowedTools "mcp__browserai__browserai_list" --permission-mode acceptEdits \
    --output-format stream-json --verbose --debug-file "$F/out/$NAME.debug.log" \
    "List the BrowserAI sessions under the directory, four times." \
    > "$W/out/$NAME.stream.jsonl" 2> "$W/out/$NAME.stderr.txt" < /dev/null

echo "claude exit=$?" >> "$W/out/$NAME.meta"
kill $STUB 2>/dev/null
sleep 1

echo "--- launches ---"
cat "$W/logs/$NAME.launches.log" 2>/dev/null
echo "--- server processes launched ---"
grep -c '^.* LAUNCH ' "$W/logs/$NAME.launches.log" 2>/dev/null
