#!/bin/bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# add-mcp 2.4.1 against Claude Code with CLAUDE_CONFIG_DIR set, in a sandbox.
# Every path below is under .work/upstream-drafts/addmcp-sandbox.
D=/c/Source/SixFive7/BrowserAI/.work/upstream-drafts
source "$D/rig/sandbox-env.sh"
OUT="$S/out"
ADDMCP="$(W $D/addmcp241/node_modules/add-mcp/dist/index.js)"
LIB="$(W $D/addmcp241/node_modules/add-mcp/dist/lib.js)"
CLAUDE="$S/bin/claude.exe"
NODE="/c/Program Files/nodejs/node.exe"
SERVER="$(W $S/server/probe-server.exe)"
LOG="$OUT/addmcp-test.log"
: > "$LOG"

files() {
  echo "--- files after: $1" >> "$LOG"
  for f in "$S/home/.claude.json" "$S/home/.claude-alt/.claude.json"; do
    if [ -f "$f" ]; then
      echo "EXISTS $(cygpath -w "$f")" >> "$LOG"
      node -e 'const j=JSON.parse(require("fs").readFileSync(process.argv[1],"utf8")); console.log("  mcpServers:", JSON.stringify(Object.keys(j.mcpServers||{})))' "$(cygpath -w "$f")" >> "$LOG" 2>&1
    else
      echo "ABSENT $(cygpath -w "$f")" >> "$LOG"
    fi
  done
}
run() {
  local label=$1; shift
  echo "=== $label :: $*" >> "$LOG"
  sbx "$@" < /dev/null >> "$LOG" 2>&1
  echo "rc=$?" >> "$LOG"
}

echo "add-mcp $(sbx "$NODE" "$ADDMCP" --version 2>&1 | tail -1); claude $(sbx "$CLAUDE" --version 2>&1 | tail -1)" >> "$LOG"
sbx /usr/bin/printenv CLAUDE_CONFIG_DIR >> "$LOG"
files "start"

# 1. add-mcp command line, global scope, Claude Code only.
run "addmcp-cli-add" "$NODE" "$ADDMCP" "$SERVER" -g -a claude-code -n probe-cli -y
files "addmcp-cli-add"

# 2. add-mcp SDK, the same thing.
run "addmcp-sdk-upsert" "$NODE" --input-type=module -e "import { pathToFileURL } from 'node:url'; const lib = await import(pathToFileURL(process.argv[1]).href); console.log(JSON.stringify(lib.upsertServer('claude-code', 'probe-sdk', { command: process.argv[2], args: [] })));" "$LIB" "$SERVER"
files "addmcp-sdk-upsert"

# 3. What add-mcp lists for Claude Code.
run "addmcp-list" "$NODE" "$ADDMCP" list -g -a claude-code
# 4. Claude Code: is either entry there? remove reads the config and says so.
run "claude-remove-probe-cli" "$CLAUDE" mcp remove probe-cli --scope user
run "claude-remove-probe-sdk" "$CLAUDE" mcp remove probe-sdk --scope user
files "claude-removes"

# 5. Positive control: Claude Code's own add, then its own remove.
run "claude-add-own" "$CLAUDE" mcp add probe-own --scope user -- "$SERVER"
files "claude-add-own"
run "claude-remove-own" "$CLAUDE" mcp remove probe-own --scope user
files "claude-remove-own"
echo "DONE" >> "$LOG"
