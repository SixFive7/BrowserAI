#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig: takes the machine-wide suite lock (.work\locks\suite, mkdir protocol, per
# .work\overnight\RULES.txt), runs the given plans through chain.sh, and releases the lock on
# every way out. Gives up (without running anything) if the lock is not free within MAXWAIT s.
# Usage: bash rig/locked-chain.sh <tag> <plan> [<plan> ...]
TAG="$1"; shift
LOCK=/c/Source/SixFive7/BrowserAI/.work/locks/suite
MAXWAIT=${MAXWAIT:-1500}
cd /c/Source/SixFive7/BrowserAI/.work/hard-kill || exit 1
waited=0
until mkdir "$LOCK" 2>/dev/null; do
  if [ "$waited" -ge "$MAXWAIT" ]; then echo "=== $TAG gave up waiting for the suite lock after ${waited}s $(date -u +%Y-%m-%dT%H:%M:%SZ)"; exit 3; fi
  sleep 30; waited=$((waited + 30))
done
trap 'rm -rf "$LOCK"; echo "=== $TAG released the suite lock $(date -u +%Y-%m-%dT%H:%M:%SZ)"' EXIT
{
  echo "lane: S3b hard-kill survival research (research agent, not a lane)"
  echo "startUtc: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "running: headless Chromium/Firefox from .work\\browsers-cache via the payload's playwright-core and @playwright/mcp; LOCALAPPDATA, TEMP, TMP and PWTEST_SERVER_REGISTRY redirected into .work\\hard-kill; no BrowserAI.Server.exe, BrowserAI.exe, installer or gate. Plans: $*"
} > "$LOCK/owner.txt"
echo "=== $TAG took the suite lock after waiting ${waited}s $(date -u +%Y-%m-%dT%H:%M:%SZ)"
bash rig/chain.sh "$@"
