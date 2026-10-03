#!/bin/bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Batch 4 of lane stale, 2026-10-03: rows 163 to 166, the four dashboard rigs,
# from copies whose one change is REPO pointing at this lane's worktree (and so
# its 0.0.83 payload, and C at the worktree's own .work/zoomout/c). The browsers
# path names a scratch root holding chromium-1247 and chromium_headless_shell-1247.
set -u
W="C:/Source/SixFive7/BrowserAI/.work/wt/stale"
S="C:/Source/SixFive7/BrowserAI/.work/stale-scratch"
NODE="$W/payload/node/node.exe"
RIG="$W/.work/zoomout/c/rig"
export PWTEST_SERVER_REGISTRY='C:\Source\SixFive7\BrowserAI\.work\wt\stale\.work\zoomout\c\reg'
export PWTEST_SOCKETS_DIR='C:\Source\SixFive7\BrowserAI\.work\wt\stale\.work\zoomout\c\sockets'
export PLAYWRIGHT_BROWSERS_PATH='C:\Source\SixFive7\BrowserAI\.work\stale-scratch\pw-browsers'
export PLAYWRIGHT_SKIP_BROWSER_GC=1 PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
L="$S/out/batch4"; mkdir -p "$L"
stamp() { date -u +%Y-%m-%dT%H:%M:%SZ; }
step() { echo "=== $(stamp) BEGIN $1" | tee -a "$L/batch4.log"; }
done_() { echo "=== $(stamp) END $1 exit=$2" | tee -a "$L/batch4.log"; }
echo "real registry before: $(ls "$LOCALAPPDATA/ms-playwright/b" 2>&1 | tr '\n' ' ')" | tee -a "$L/batch4.log"

step dashboard-probe; "$NODE" "$RIG/dashboard-probe.cjs" > "$L/dashboard-probe.log" 2>&1; done_ dashboard-probe $?
step mcp-pause-probe; "$NODE" "$RIG/mcp-pause-probe.cjs" > "$L/mcp-pause-probe.log" 2>&1; done_ mcp-pause-probe $?
step singleton-probe; "$NODE" "$RIG/singleton-probe.cjs" > "$L/singleton-probe.log" 2>&1; done_ singleton-probe $?
step trace-viewer-probe; "$NODE" "$RIG/trace-viewer-probe.cjs" > "$L/trace-viewer-probe.log" 2>&1; done_ trace-viewer-probe $?

echo "real registry after: $(ls "$LOCALAPPDATA/ms-playwright/b" 2>&1 | tr '\n' ' ')" | tee -a "$L/batch4.log"
echo "=== $(stamp) BATCH4 COMPLETE" | tee -a "$L/batch4.log"
