#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03): runs plans one after another, never two at once.
# Usage: bash rig/chain.sh <plan-name> [<plan-name> ...]   (from .work/durability)
export DOTNET_CLI_TELEMETRY_OPTOUT=1 GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=never
cd /c/Source/SixFive7/BrowserAI/.work/durability || exit 1
for plan in "$@"; do
  echo "=== $plan start $(date -u +%Y-%m-%dT%H:%M:%SZ)"
  pwsh -NoProfile -NonInteractive -File rig/orchestrate.ps1 -PlanFile "plans/$plan.json" -ResultsFile "results/$plan.jsonl" >> "results/$plan.log" 2>&1
  echo "=== $plan exit=$? $(date -u +%Y-%m-%dT%H:%M:%SZ)"
done
echo "=== chain done $(date -u +%Y-%m-%dT%H:%M:%SZ)"
