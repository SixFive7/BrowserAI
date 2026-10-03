#!/bin/bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Takes the suite lock for lane stale, polling every 2 s, writes owner.txt, and
# exits holding it. Releasing is the caller's job: release-lock.sh.
set -u
L="C:/Source/SixFive7/BrowserAI/.work/locks/suite"
OUT="C:/Source/SixFive7/BrowserAI/.work/stale-scratch/out/lock-state.txt"
stamp() { date -u +%Y-%m-%dT%H:%M:%SZ; }
echo "$(stamp) waiting" > "$OUT"
until mkdir "$L" 2>/dev/null; do sleep 2; done
printf 'lane: stale\nstarted: %s\nrunning: %s\n' "$(stamp)" "${1:-lane stale}" > "$L/owner.txt"
echo "$(stamp) TAKEN" >> "$OUT"
