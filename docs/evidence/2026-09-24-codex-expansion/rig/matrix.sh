#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Q288 scratch rig: the whole launch matrix, three rounds.
set -u
cd /c/Source/SixFive7/BrowserAI/.work/codex-expansion/rig
echo "MATRIX START $(date -u +%FT%TZ)"
for r in 1 2 3; do
  for sp in braced bare percent tilde; do
    for kind in user project; do
      out=$(node setup.js "$kind" "$sp" "$r" 2>&1); echo "$out" | head -1
      dir=$(echo "$out" | tail -1)
      node appsrv.js "$dir" 2>&1
    done
  done
  for kind in alt untrusted; do
    out=$(node setup.js "$kind" - "$r" 2>&1); echo "$out" | head -1
    dir=$(echo "$out" | tail -1)
    node appsrv.js "$dir" 2>&1
  done
done
echo "MATRIX END $(date -u +%FT%TZ)"
