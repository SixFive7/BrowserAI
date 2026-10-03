#!/bin/bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Extra Path A runs, to read the rate of the Firefox localStorage loss the first two showed.
set -u
W="C:/Source/SixFive7/BrowserAI/.work/wt/stale"; S="C:/Source/SixFive7/BrowserAI/.work/stale-scratch"
NODE="$W/payload/node/node.exe"
EXE='C:\Source\SixFive7\BrowserAI\.work\wt\stale\src\BrowserAI\bin\Release\net10.0-windows\win-x64\publish\BrowserAI.Server.exe'
OUTW='C:\Source\SixFive7\BrowserAI\.work\stale-scratch\out'
export TEMP="C:\Source\SixFive7\BrowserAI\.work\stale-scratch\tmp" TMP="C:\Source\SixFive7\BrowserAI\.work\stale-scratch\tmp"
L="$S/out/batch3"
for tag in A-firefox-3 A-firefox-4 A-firefox-5 A-firefox-6 A-chromium-3 A-chromium-4; do
  fam=${tag#A-}; fam=${fam%-*}
  echo "=== $(date -u +%Y-%m-%dT%H:%M:%SZ) BEGIN row38 $tag" | tee -a "$L/batch3.log"
  "$NODE" "$S/rigs/row38/resume-probe2.js" "$EXE" "$OUTW\row38\session-$tag" "$OUTW\row38\$tag.json" "$fam" > "$L/row38-$tag.log" 2>&1
  echo "=== $(date -u +%Y-%m-%dT%H:%M:%SZ) END row38 $tag exit=$?" | tee -a "$L/batch3.log"
done
echo "=== $(date -u +%Y-%m-%dT%H:%M:%SZ) BATCH3B COMPLETE" | tee -a "$L/batch3.log"
