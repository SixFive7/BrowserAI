#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig: runs plans one after another (never two orchestrators at once), with a
# read-only snapshot of the outside state before and after each one.
# Usage: bash rig/chain.sh <plan-name> [<plan-name> ...]   (from .work/hard-kill)
export DOTNET_CLI_TELEMETRY_OPTOUT=1 GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=never
cd /c/Source/SixFive7/BrowserAI/.work/hard-kill || exit 1
for plan in "$@"; do
  echo "=== $plan start $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  pwsh -NoProfile -NonInteractive -File rig/snap-external.ps1 -Label "before $plan" > /dev/null 2>&1
  pwsh -NoProfile -NonInteractive -File rig/orchestrate.ps1 -PlanFile "plans/$plan.json" -ResultsFile "results/$plan.jsonl" >> "results/$plan.log" 2>&1
  echo "=== $plan exit=$? $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  pwsh -NoProfile -NonInteractive -File rig/snap-external.ps1 -Label "after $plan" > /dev/null 2>&1
done
echo "=== chain done $(date -u +%Y-%m-%dT%H:%M:%SZ)"
