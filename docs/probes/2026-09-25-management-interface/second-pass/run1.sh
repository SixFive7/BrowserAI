#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# run1.sh <label> <mode> <timeoutSec> <sampleDelaysCsv> [probe options and -- chrome flags]
# One probe run on a private desktop inside a kill-on-close job, with LOCALAPPDATA and TEMP in scratch.
cd "$(dirname "$0")"
label=$1; mode=$2; timeout=$3; delays=$4; shift 4
CHROME="C:/Source/SixFive7/BrowserAI/.work/zoomout/d/bench/browsers/chromium-1246/chrome-win64/chrome.exe"
B2="C:/Source/SixFive7/BrowserAI/.work/zoomout/b2"
[ -n "$KEEP" ] || rm -rf "profiles/$label"
env LOCALAPPDATA="$B2/localappdata" TEMP="$B2/tmp" TMP="$B2/tmp" MSYS2_ARG_CONV_EXCL='*' out/driver/driver.exe desk "$timeout" "$B2/run/$label.ready" "$delays" "$B2/out/p1probe/p1probe.exe" "$CHROME" "$B2/profiles/$label" "$B2/run" "$label" "$mode" "$@" > "logs/run-$label.log" 2>&1
echo "driver exit $?" >> "logs/run-$label.log"
