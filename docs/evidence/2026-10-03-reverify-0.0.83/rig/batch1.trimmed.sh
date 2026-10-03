#!/bin/bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Batch 1 of lane stale, 2026-10-03: rows 140, 121, 22, 141, 122, 109, 152 and 95.
# Run under the suite lock. Every arm is headless or launches no browser at all.
set -u
W="C:/Source/SixFive7/BrowserAI/.work/wt/stale"
S="C:/Source/SixFive7/BrowserAI/.work/stale-scratch"
R="$S/rigs"
NODE="$W/payload/node/node.exe"
export CORE_PATH="C:\\Source\\SixFive7\\BrowserAI\\.work\\wt\\stale\\payload\\mcp\\node_modules\\playwright-core"
export CHROME_EXE="%USERPROFILE%\\AppData\\Local\\BrowserAI\\browsers\\chromium-1247\\chrome-win64\\chrome.exe"
export SHELL_EXE="C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch\\pw-browsers\\chromium_headless_shell-1247\\chrome-headless-shell-win64\\chrome-headless-shell.exe"
export TEMP="C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch\\tmp" TMP="C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch\\tmp"
export PLAYWRIGHT_SKIP_BROWSER_GC=1 PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
L="$S/out/batch1"; mkdir -p "$L"
stamp() { date -u +%Y-%m-%dT%H:%M:%SZ; }
step() { echo "=== $(stamp) BEGIN $1" | tee -a "$L/batch1.log"; }
done_() { echo "=== $(stamp) END $1 exit=$2" | tee -a "$L/batch1.log"; }

step row140; bash "$R/row140/eftype.sh" > "$L/row140.log" 2>&1; done_ row140 $?

step row121; "$NODE" "$R/row121/sandbox-arms.js" 2 > "$L/row121.log" 2>&1; done_ row121 $?

step row22; mkdir -p "$S/out/row22"; BASE="C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch\\out\\row22" "$NODE" "$R/row22/profile.js" concurrent > "$L/row22.log" 2>&1; done_ row22 $?

step row141a; "$NODE" "$R/row141/shots.js" > "$L/row141-shots.log" 2>&1; done_ row141a $?
step row141b; "$NODE" "$R/row141/shots2.js" > "$L/row141-shots2.log" 2>&1; done_ row141b $?

step row122; "$NODE" "$R/row122/raw-child.js" chromium > "$L/row122.log" 2>&1; done_ row122 $?

step row109; "$NODE" "$R/row109/ua-probe.js" > "$L/row109.log" 2>&1; done_ row109 $?

step row152a; "$NODE" "$R/row152/fp.mjs" funnel headless-plain --headless > "$L/row152-plain.log" 2>&1; done_ row152a $?
step row152b; "$NODE" "$R/row152/fp.mjs" funnel headless-auto --headless --enable-automation > "$L/row152-auto.log" 2>&1; done_ row152b $?

step row95; "$NODE" "$R/row95/cookie-probe.js" > "$L/row95.log" 2>&1; done_ row95 $?

echo "=== $(stamp) BATCH1 COMPLETE" | tee -a "$L/batch1.log"
