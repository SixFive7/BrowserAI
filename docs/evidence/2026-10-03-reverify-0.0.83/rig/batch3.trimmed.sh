#!/bin/bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Batch 3 of lane stale, 2026-10-03: rows 34 and 38 in ONE sitting, through the
# published BrowserAI.Server.exe of the lane's worktree (origin/next 5f1166c,
# payload @playwright/mcp 0.0.83). The rigs run as stored in docs/probes.
#   row 34: ratios-probe.js, six rounds per family, families alternated, one per process run.
#   row 38: Path A = resume-probe2.js, Path B = selfdeath-probe.js kill, twice per path per family.
set -u
W="C:/Source/SixFive7/BrowserAI/.work/wt/stale"
S="C:/Source/SixFive7/BrowserAI/.work/stale-scratch"
NODE="$W/payload/node/node.exe"
EXE='C:\Source\SixFive7\BrowserAI\.work\wt\stale\src\BrowserAI\bin\Release\net10.0-windows\win-x64\publish\BrowserAI.Server.exe'
BROWSERS='%USERPROFILE%\AppData\Local\BrowserAI\browsers'
OUTW='C:\Source\SixFive7\BrowserAI\.work\stale-scratch\out'
RATIOS="$S/rigs/row34/ratios-probe.js"
PATHA="$S/rigs/row38/resume-probe2.js"
PATHB="$S/rigs/row38/selfdeath-probe.js"
export TEMP="C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch\\tmp" TMP="C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch\\tmp"
L="$S/out/batch3"; mkdir -p "$L" "$S/out/row34" "$S/out/row38"
stamp() { date -u +%Y-%m-%dT%H:%M:%SZ; }
step() { echo "=== $(stamp) BEGIN $1" | tee -a "$L/batch3.log"; }
done_() { echo "=== $(stamp) END $1 exit=$2" | tee -a "$L/batch3.log"; }

for n in 1 2 3 4 5 6; do
  for fam in chromium firefox; do
    tag="$fam-$n"
    step "row34 $tag"
    "$NODE" "$RATIOS" "$EXE" "$OUTW\\row34\\session-$tag" "$OUTW\\row34\\$tag.json" "$fam" 30 "$BROWSERS" > "$L/row34-$tag.log" 2>&1
    done_ "row34 $tag" $?
  done
done

for n in 1 2; do
  for fam in chromium firefox; do
    tag="A-$fam-$n"
    step "row38 $tag"
    "$NODE" "$PATHA" "$EXE" "$OUTW\\row38\\session-$tag" "$OUTW\\row38\\$tag.json" "$fam" > "$L/row38-$tag.log" 2>&1
    done_ "row38 $tag" $?
    tag="B-$fam-$n"
    step "row38 $tag"
    "$NODE" "$PATHB" "$EXE" "$OUTW\\row38\\session-$tag" "$OUTW\\row38\\$tag.json" kill "$fam" > "$L/row38-$tag.log" 2>&1
    done_ "row38 $tag" $?
  done
done

echo "=== $(stamp) BATCH3 COMPLETE" | tee -a "$L/batch3.log"
