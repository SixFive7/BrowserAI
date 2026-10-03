#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Hashes the maintainer's real client configuration files, read-only.
# Usage: hash-real.sh <label>   -> writes hashes/<label>.txt
# Second look, 2026-10-01. Same file list as .work/zoomout/a/hash-real.sh plus OutlookAI's
# registry record and both repositories' git state. Nothing here writes outside scratch.
set -u
S=/c/Source/SixFive7/BrowserAI/.work/zoomout/a-second-look
mkdir -p "$S/hashes"
OUT=$S/hashes/$1.txt
H=%USERPROFILE%
AD="$H/AppData/Roaming"
LAD="$H/AppData/Local"
REPO=/c/Source/SixFive7/BrowserAI
OREPO=/c/Source/SixFive7/OutlookAI
{
echo "# label=$1 at $(date -u +%Y-%m-%dT%H:%M:%SZ)"
for f in \
  "$H/.claude.json" "$H/.claude/settings.json" "$H/.claude/settings.local.json" "$H/.claude/.mcp.json" \
  "$H/.claude/CLAUDE.md" "$H/.claude/plugins/installed_plugins.json" "$H/.claude/plugins/known_marketplaces.json" \
  "$H/.codex/config.toml" "$H/.codex/AGENTS.md" \
  "$AD/Claude/claude_desktop_config.json" "$AD/Claude/config.json" \
  "$AD/Code/User/settings.json" "$AD/Code/User/mcp.json" \
  "$AD/Code - Insiders/User/mcp.json" "$AD/Cursor/User/settings.json" "$H/.cursor/mcp.json" \
  "$H/.gemini/settings.json" "$H/.codeium/windsurf/mcp_config.json" "$AD/Zed/settings.json" \
  "$H/.config/opencode/opencode.json" "$H/.continue/config.yaml" "$H/.kiro/settings/mcp.json" \
  "$H/.qwen/settings.json" "$H/.aws/amazonq/mcp.json" "$H/.copilot/mcp-config.json" \
  "$AD/Code/User/globalStorage/saoudrizwan.claude-dev/settings/cline_mcp_settings.json" \
  "$AD/Block/goose/config/config.yaml" "$H/.lmstudio/mcp.json" "$H/.npmrc" "$H/.gitconfig" \
  "$REPO/.mcp.json" "$REPO/.vscode/mcp.json" "$REPO/.cursor/mcp.json" "$REPO/.codex/config.toml" \
  "$OREPO/.mcp.json" \
  "$LAD/BrowserAI/mcp-registration.json" ; do
  if [ -f "$f" ]; then
    printf '%s  %s  size=%s mtime=%s\n' "$(sha256sum "$f" | cut -d' ' -f1)" "$f" "$(stat -c %s "$f")" "$(stat -c %Y "$f")"
  else
    printf 'ABSENT  %s\n' "$f"
  fi
done
# The mcpServers subtree of ~/.claude.json is the part a registrar would touch.
# The whole file is rewritten by the running Claude Code for its own bookkeeping,
# so the full-file hash moves on its own and this line is the one to compare.
"/c/Program Files/nodejs/node.exe" -e '
const fs=require("fs");const p=process.argv[1];
try{const j=JSON.parse(fs.readFileSync(p,"utf8"));
const c=require("crypto");
const top=JSON.stringify(j.mcpServers??null);
const proj={};for(const [k,v] of Object.entries(j.projects??{})){if(v&&v.mcpServers&&Object.keys(v.mcpServers).length)proj[k]=v.mcpServers;}
console.log("claude.json mcpServers sha256="+c.createHash("sha256").update(top).digest("hex")+" names="+Object.keys(j.mcpServers??{}).join(","));
console.log("claude.json projects[*].mcpServers sha256="+c.createHash("sha256").update(JSON.stringify(proj)).digest("hex")+" projectsWithServers="+Object.keys(proj).length);
}catch(e){console.log("claude.json unreadable: "+e.message)}' "$H/.claude.json"
echo "## ~/.codex top level (mode size name)"; ls -la "$H/.codex" 2>&1 | awk '{print $1,$5,$9}' | sort -k3
echo "## ~/.codex/tmp recursive"; find "$H/.codex/tmp" -maxdepth 3 2>/dev/null | sort
echo "## ~/.claude top level names"; ls -1a "$H/.claude" 2>&1 | sort | tr '\n' ' '; echo
echo "## HKCU\\Environment Path sha256"; reg query 'HKCU\Environment' //v Path 2>&1 | tr -d '\r' | sha256sum | cut -d' ' -f1
echo "## HKCU\\Environment all values sha256"; reg query 'HKCU\Environment' 2>&1 | tr -d '\r' | sha256sum | cut -d' ' -f1
echo "## HKCU\\Software\\OutlookAI\\Mcp (minus LastReconcileUtc) sha256"; reg query 'HKCU\Software\OutlookAI\Mcp' 2>&1 | tr -d '\r' | grep -v LastReconcileUtc | sha256sum | cut -d' ' -f1
echo "## installed BrowserAI top-level"; ls -1 "$LAD/BrowserAI.app" 2>&1 | tr '\n' ' '; echo; ls -1 "$LAD/BrowserAI" 2>&1 | tr '\n' ' '; echo
echo "## home dotfiles created by tools (names)"; ls -1a "$H" 2>&1 | grep -E '^\.(mcpm|smithery|toolhive|thv|mcp|config|cursor|gemini|codeium|continue|kiro|qwen|copilot|lmstudio|docker|add-mcp|install-mcp|ruler|rulesync|fastmcp|apm)' | tr '\n' ' '; echo
echo "## BrowserAI git status / head"; git -C "$REPO" rev-parse HEAD; git -C "$REPO" status --short | head -20
echo "## OutlookAI git status / head"; git -C "$OREPO" rev-parse HEAD; git -C "$OREPO" status --short | head -20
} > "$OUT" 2>&1
echo "wrote $OUT"
