#!/bin/bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Row 152, the raw playwright-core half, headless only: two arms of fp.mjs in pw mode
# (plain, and with --disable-blink-features=AutomationControlled), Chromium 1247.
# Waits for the suite lock, takes it, runs, releases it on every path.
set -u
S="C:/Source/SixFive7/BrowserAI/.work/stale-scratch"
W="C:/Source/SixFive7/BrowserAI/.work/wt/stale"
L="C:/Source/SixFive7/BrowserAI/.work/locks/suite"
O="$S/out/row152-pw"; mkdir -p "$O"
LOG="$O/driver.log"
stamp() { date -u +%Y-%m-%dT%H:%M:%SZ; }
echo "$(stamp) waiting for the suite lock" >> "$LOG"
until mkdir "$L" 2>/dev/null; do sleep 30; done
printf 'lane: stale\nstarted: %s\nrunning: %s\n' "$(stamp)" "re-verification row 152, the raw playwright-core half: two headless Chromium 1247 launches through fp.mjs pw (about 20 s); no window" > "$L/owner.txt"
trap 'if grep -q "^lane: stale$" "$L/owner.txt" 2>/dev/null; then rm -f "$L/owner.txt"; rmdir "$L"; echo "$(stamp) lock released" >> "$LOG"; fi' EXIT
echo "$(stamp) lock taken" >> "$LOG"
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
