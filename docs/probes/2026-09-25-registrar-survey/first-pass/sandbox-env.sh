# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Source with: . sandbox-env.sh <run-name> [claude-config-dir-mode]
# mode "aligned" (default): CLAUDE_CONFIG_DIR = sandbox home, so $CLAUDE_CONFIG_DIR/.claude.json == ~/.claude.json
# mode "separate": CLAUDE_CONFIG_DIR = <home>\.claude-alt, a directory Claude Code honours and a tool that ignores the variable does not
A=/c/Source/SixFive7/BrowserAI/.work/zoomout/a
RUN=$1; MODE=${2:-aligned}
S=$A/sandbox/$RUN
mkdir -p "$S/Zoë O'Brien/AppData/Roaming" "$S/Zoë O'Brien/AppData/Local" "$S/Zoë O'Brien/.codex" "$S/tmp" "$S/project"
W() { cygpath -w "$1"; }
export USERPROFILE="$(W "$S/Zoë O'Brien")" HOME="$(W "$S/Zoë O'Brien")" HOMEDRIVE="" HOMEPATH=""
export APPDATA="$(W "$S/Zoë O'Brien/AppData/Roaming")" LOCALAPPDATA="$(W "$S/Zoë O'Brien/AppData/Local")"
export XDG_CONFIG_HOME="$(W "$S/Zoë O'Brien/.config")"
export CODEX_HOME="$(W "$S/Zoë O'Brien/.codex")"; export CODEX_HOME_U="$S/Zoë O'Brien/.codex"
if [ "$MODE" = separate ]; then mkdir -p "$S/Zoë O'Brien/.claude-alt"; export CLAUDE_CONFIG_DIR="$(W "$S/Zoë O'Brien/.claude-alt")"; else export CLAUDE_CONFIG_DIR="$(W "$S/Zoë O'Brien")"; fi
export TEMP="$(W "$S/tmp")" TMP="$(W "$S/tmp")"
export DISABLE_AUTOUPDATER=1 CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1 DISABLE_TELEMETRY=1 DISABLE_ERROR_REPORTING=1
export npm_config_cache="$(W "$A/npm-cache")" npm_config_userconfig="$(W "$A/npmrc-empty")" npm_config_update_notifier=false
export NO_COLOR=1 FORCE_COLOR=0 CI=1
export PATH="$A/bin:/c/Program Files/nodejs:/c/Windows/System32:/c/Windows:/usr/bin:/mingw64/bin"
cd "$S/project"
echo "sandbox=$S mode=$MODE USERPROFILE=$USERPROFILE CLAUDE_CONFIG_DIR=$CLAUDE_CONFIG_DIR CODEX_HOME=$CODEX_HOME"
