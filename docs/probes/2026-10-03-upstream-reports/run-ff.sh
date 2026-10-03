#!/bin/bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Runs the safe-mode arms one at a time against one Playwright + Firefox pair.
# Usage: run-ff.sh <label> <playwright-core dir> <firefox revision> <arms...>
D=/c/Source/SixFive7/BrowserAI/.work/upstream-drafts
W() { cygpath -w "$1"; }
LABEL=$1; PWDIR=$2; REV=$3; shift 3
export LOCALAPPDATA="$(W $D/localappdata)" APPDATA="$(W $D/appdata)" TEMP="$(W $D/tmp)" TMP="$(W $D/tmp)"
export PLAYWRIGHT_BROWSERS_PATH="$(W $D/browsers)" PWTEST_SERVER_REGISTRY="$(W $D/registry)" PWTEST_SOCKETS_DIR="$(W $D/sockets)"
mkdir -p "$D/registry" "$D/sockets"
OUT="$D/runs/ff-$LABEL"
mkdir -p "$OUT"
for arm in "$@"; do
  echo "$(date -u +%FT%TZ) arm=$arm start" >> "$OUT/runner.log"
  node "$(W $D/rig/ff-safemode.cjs)" "$(W $PWDIR)" "$(W $D/browsers/$REV/firefox/firefox.exe)" "$arm" "$(W $OUT)" </dev/null >> "$OUT/runner.log" 2>&1
  echo "$(date -u +%FT%TZ) arm=$arm rc=$?" >> "$OUT/runner.log"
done
echo "ALL DONE" >> "$OUT/runner.log"
