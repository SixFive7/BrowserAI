# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Sourced. Builds a minimal sandboxed environment for add-mcp and Claude Code.
S=/c/Source/SixFive7/BrowserAI/.work/upstream-drafts/addmcp-sandbox
W() { cygpath -w "$1"; }
SANDBOX_ENV=(
  "SystemRoot=C:\Windows" "windir=C:\Windows" "SystemDrive=C:" "COMSPEC=C:\Windows\System32\cmd.exe"
  "PATHEXT=.COM;.EXE;.BAT;.CMD" "PATH=C:\Program Files\nodejs;C:\Windows\System32;C:\Windows"
  "USERPROFILE=$(W $S/home)" "HOME=$(W $S/home)" "HOMEDRIVE=" "HOMEPATH="
  "APPDATA=$(W $S/home/AppData/Roaming)" "LOCALAPPDATA=$(W $S/home/AppData/Local)"
  "TEMP=$(W $S/tmp)" "TMP=$(W $S/tmp)"
  "CLAUDE_CONFIG_DIR=$(W $S/home/.claude-alt)" "CODEX_HOME=$(W $S/home/.codex)"
  "DISABLE_AUTOUPDATER=1" "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1" "DISABLE_TELEMETRY=1" "DISABLE_ERROR_REPORTING=1"
  "NO_COLOR=1" "CI=1" "npm_config_update_notifier=false"
)
sbx() { env -i "${SANDBOX_ENV[@]}" "$@"; }
