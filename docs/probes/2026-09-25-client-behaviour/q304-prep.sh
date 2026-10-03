#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Q304 a, phase 1: the before-readings and every scratch directory, under the lock.
set -u
W=/c/Source/SixFive7/BrowserAI/.work/client-behaviour
F=C:/Source/SixFive7/BrowserAI/.work/client-behaviour
Q="$W/q304"; QF="$F/q304"
NODE="C:/Program Files/nodejs/node.exe"
CODEX="%USERPROFILE%/AppData/Local/OpenAI/Codex/bin/247581e40ee272fb/codex.exe"
SCRATCH_W='%USERPROFILE%\Downloads\tmp-client-behaviour\q304'
H="pwsh -NoProfile -NonInteractive -File $F/rig/q304-helpers.ps1"

grep -q "TAKEN" "$Q/lock.log" || { echo "the installer lock is not held -- stop"; exit 2; }

mkdir -p "$Q/readings" "$Q/runs" "$Q/logs"
$H -Do clearance -Out "$QF/readings/1-before.clearance.txt"
$H -Do user-path -Out "$QF/readings/1-before.user-path.json"
$H -Do shell-path -Out "$QF/readings/1-before.shell-path.json"
$H -Do fresh-env -Out "$QF/readings/1-before.fresh-env.json"

# The install root and the data root must be inside the profile (InstallRootScope), so
# they are the one thing outside .work: %USERPROFILE%\Downloads\tmp-client-behaviour\q304.
mkdir -p "%USERPROFILE%/Downloads/tmp-client-behaviour/q304/data"
rm -rf "%USERPROFILE%/Downloads/tmp-client-behaviour/q304/root"

# The installer, copied so a gate driver re-packing Releases\test-pack never meets a file in use.
cp -p /c/Source/SixFive7/BrowserAI/Releases/test-pack/BrowserAI.test-installer.exe "$Q/BrowserAI.test-installer.exe"
sha256sum /c/Source/SixFive7/BrowserAI/Releases/test-pack/BrowserAI.test-installer.exe "$Q/BrowserAI.test-installer.exe" > "$Q/readings/installer.sha256"
cp -p /c/Source/SixFive7/BrowserAI/Releases/test-pack/releases.win.json "$Q/readings/test-pack.releases.win.json"

# The hooks' own sandbox: CLAUDE_CONFIG_DIR seeded as onboarded, and a CODEX_HOME that exists.
mkdir -p "$Q/hook-claude" "$Q/hook-codexhome"
printf '%s' '{"hasCompletedOnboarding":true,"autoUpdates":false,"bypassPermissionsModeAccepted":false}' > "$Q/hook-claude/.claude.json"

# The trusted project: its own git root (so Codex takes it, and not this repository, as the
# project), with the entry written the way the product writes one -- by the client's own
# `codex mcp add` with CODEX_HOME at <project>\.codex -- naming BrowserAI.Server.exe ALONE.
# The one addition is env.BROWSERAI_ROOT, so the installed server's data root (and its
# machine-wide stray sweep) is scratch and never %LOCALAPPDATA%\BrowserAI; PATH is not in it,
# so the resolver still searches the PATH Codex itself inherited.
rm -rf "$Q/proj"; mkdir -p "$Q/proj/.codex"
git init -q "$Q/proj"
PROJ_W="$(cygpath -w "$Q/proj")"
( export CODEX_HOME="$(cygpath -w "$Q/proj/.codex")"; unset CLAUDECODE CLAUDE_CODE_ENTRYPOINT CLAUDE_CODE_SSE_PORT
  "$CODEX" mcp add browserai --env "BROWSERAI_ROOT=$SCRATCH_W\\data" -- BrowserAI.Server.exe > "$Q/logs/mcp-add.out" 2>&1; echo "mcp add exit=$?" >> "$Q/logs/mcp-add.out"
  "$CODEX" mcp get browserai --json > "$Q/logs/mcp-get.json" 2>> "$Q/logs/mcp-add.out"; echo "mcp get exit=$?" >> "$Q/logs/mcp-add.out" )
cp "$Q/proj/.codex/config.toml" "$Q/readings/project-config.toml"
ls -la "$Q/proj/.codex" > "$Q/readings/project-dotcodex-listing.txt" 2>&1

# One CODEX_HOME per app-server run: a model provider that points at nothing (no turn is
# ever taken), no MCP server of its own, and the project trusted.
for R in B1 B2 B3 A1 A2 A3; do
  mkdir -p "$Q/runs/$R/home"
  cat > "$Q/runs/$R/home/config.toml" <<CFG
model = "stub-model"
model_provider = "stub"
approval_policy = "never"
sandbox_mode = "read-only"

[model_providers.stub]
name = "stub"
base_url = "http://127.0.0.1:9/v1"
wire_api = "responses"
env_key = "Q304_STUB_KEY"

[projects.'$PROJ_W']
trust_level = "trusted"
CFG
done
echo "prepared; project=$PROJ_W"
cat "$Q/logs/mcp-add.out"; cat "$Q/readings/project-config.toml"; cat "$Q/logs/mcp-get.json"
