#!/bin/bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# The add-mcp draft's repro, run as written, in a fresh sandbox home.
# USERPROFILE/HOME and CLAUDE_CONFIG_DIR are two different empty directories under scratch.
D=/c/Source/SixFive7/BrowserAI/.work/upstream-drafts
S=$D/addmcp-sandbox
H=$S/home3
W() { cygpath -w "$1"; }
rm -rf "$H"
mkdir -p "$H/AppData/Roaming" "$H/AppData/Local" "$S/cfg3"
rm -rf "$S/cfg3"/* "$S/cfg3"/.[!.]* 2>/dev/null
ENV=(
  "SystemRoot=C:\\Windows" "windir=C:\\Windows" "SystemDrive=C:" "COMSPEC=C:\\Windows\\System32\\cmd.exe"
  "PATHEXT=.COM;.EXE;.BAT;.CMD" "PATH=C:\\Program Files\\nodejs;C:\\Windows\\System32;C:\\Windows"
  "USERPROFILE=$(W $H)" "HOME=$(W $H)" "HOMEDRIVE=" "HOMEPATH="
  "APPDATA=$(W $H/AppData/Roaming)" "LOCALAPPDATA=$(W $H/AppData/Local)"
  "TEMP=$(W $S/tmp)" "TMP=$(W $S/tmp)"
  "CLAUDE_CONFIG_DIR=$(W $S/cfg3)" "CODEX_HOME=$(W $H/.codex)"
  "DISABLE_AUTOUPDATER=1" "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1" "DISABLE_TELEMETRY=1" "DISABLE_ERROR_REPORTING=1"
  "NO_COLOR=1" "CI=1" "npm_config_update_notifier=false"
)
sbx() { env -i "${ENV[@]}" "$@"; }
NODE="/c/Program Files/nodejs/node.exe"
ADDMCP="$(W $D/addmcp241/node_modules/add-mcp/dist/index.js)"
CLAUDE="$S/bin/claude.exe"
LOG="$S/out/addmcp-repro.log"
{
  echo "USERPROFILE=$(sbx /usr/bin/printenv USERPROFILE)"
  echo "CLAUDE_CONFIG_DIR=$(sbx /usr/bin/printenv CLAUDE_CONFIG_DIR)"
  echo '$ add-mcp "node server.js" --name demo -g -a claude-code -y'
  sbx "$NODE" "$ADDMCP" "node server.js" --name demo -g -a claude-code -y </dev/null 2>&1 | sed 's/\x1b\[[0-9;?]*[a-zA-Z]//g' | grep -a -E "Claude Code|Done|Fail|rror"
  echo "rc=${PIPESTATUS[0]}"
  for f in "$H/.claude.json" "$S/cfg3/.claude.json"; do
    if [ -f "$f" ]; then echo "EXISTS $(W $f): $(cat "$f" | tr -d '\n' | tr -s ' ')"; else echo "ABSENT $(W $f)"; fi
  done
  echo '$ claude mcp remove demo --scope user'
  sbx "$CLAUDE" mcp remove demo --scope user </dev/null 2>&1; echo "rc=$?"
  echo '$ claude mcp add demo2 --scope user -- node server.js'
  sbx "$CLAUDE" mcp add demo2 --scope user -- node server.js </dev/null 2>&1; echo "rc=$?"
  echo '$ add-mcp list -g -a claude-code'
  sbx "$NODE" "$ADDMCP" list -g -a claude-code </dev/null 2>&1 | sed 's/\x1b\[[0-9;?]*[a-zA-Z]//g' | grep -a -E "Claude Code"
} > "$LOG" 2>&1
cat "$LOG"
