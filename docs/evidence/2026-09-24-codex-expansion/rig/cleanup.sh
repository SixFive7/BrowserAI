#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Q288 scratch rig: removes every scratch registration and the stub folder.
# Evidence (setup.json, result.json, driver.log, appserver.stderr.txt,
# stublogs/) stays in .work/codex-expansion/runs/<run>/.
set -u
W=/c/Source/SixFive7/BrowserAI/.work/codex-expansion
echo "CLEANUP START $(date -u +%FT%TZ)"
removed=0
for d in "$W"/runs/*/; do
  run=$(basename "$d")
  for sub in home proj neutral; do
    if [ -e "$d$sub" ]; then
      rm -rf "$d$sub" && echo "removed runs/$run/$sub" && removed=$((removed+1))
    fi
  done
done
echo "removed $removed scratch home/project directories"
if [ -e "$W/empty-home" ]; then rm -rf "$W/empty-home" && echo "removed empty-home"; fi
P=/c/Users/jori/AppData/Local/codex-expansion-probe
if [ -e "$P" ]; then
  find "$P" -type f | sed 's/^/stub folder file: /'
  rm -rf "$P" && echo "removed $P"
fi
for c in src-tag src-main; do
  if [ -e "$W/$c" ]; then rm -rf "$W/$c" && echo "removed $c clone"; fi
done
echo "left: $(ls "$W"/runs | wc -l) run records"
ls -d "$P" 2>&1
echo "CLEANUP END $(date -u +%FT%TZ)"
