#!/bin/bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Row 152, the raw playwright-core half, headless only: two arms of fp.mjs in pw mode
# (plain, and with --disable-blink-features=AutomationControlled), Chromium 1247.
# Run while lane stale holds the suite lock; it takes no lock itself.
set -u
S="C:/Source/SixFive7/BrowserAI/.work/stale-scratch"
W="C:/Source/SixFive7/BrowserAI/.work/wt/stale"
O="$S/out/row152-pw"; rm -rf "$O"; mkdir -p "$O"
LOG="$O/driver.log"
stamp() { date -u +%Y-%m-%dT%H:%M:%SZ; }
export TEMP='C:\Source\SixFive7\BrowserAI\.work\stale-scratch\tmp' TMP='C:\Source\SixFive7\BrowserAI\.work\stale-scratch\tmp'
export PLAYWRIGHT_SKIP_BROWSER_GC=1 PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
NODE="$W/payload/node/node.exe"
cd "$S/rigs/row152" || exit 1
for arm in "headless-plain --headless" "headless-nab --headless --nab"; do
  set -- $arm
  name=$1; shift
  echo "$(stamp) BEGIN pw $name $*" >> "$LOG"
  timeout 180 "$NODE" fp.mjs pw "$name" "$@" > "$O/pw-$name.log" 2>&1
  echo "$(stamp) END pw $name exit=$?" >> "$LOG"
done
echo "$(stamp) DONE" >> "$LOG"
