#!/bin/bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# lock.sh take <what> | lock.sh release
# The suite lock of RULES.txt: the directory is the lock, mkdir decides who holds it.
L="C:/Source/SixFive7/BrowserAI/.work/locks/suite"
case "$1" in
  take)
    if mkdir "$L" 2>/dev/null; then
      printf 'lane: stale\nstarted: %s\nrunning: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$2" > "$L/owner.txt"
      echo "TAKEN"
    else
      echo "BUSY"; cat "$L/owner.txt" 2>/dev/null
    fi ;;
  release)
    if grep -q '^lane: stale$' "$L/owner.txt" 2>/dev/null; then rm -f "$L/owner.txt"; rmdir "$L" && echo "RELEASED"; else echo "NOT OURS"; cat "$L/owner.txt" 2>/dev/null; fi ;;
esac
