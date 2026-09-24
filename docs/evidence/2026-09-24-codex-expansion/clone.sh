#!/usr/bin/env bash
# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
set -u
export GIT_TERMINAL_PROMPT=0
cd /c/Source/SixFive7/BrowserAI/.work/codex-expansion
echo "START $(date -u +%FT%TZ)"
git clone --depth 1 --branch rust-v0.155.0-alpha.9.2 --filter=blob:none --sparse https://github.com/openai/codex.git src-tag && (cd src-tag && git sparse-checkout set codex-rs && git rev-parse HEAD && git log -1 --format='%H %cI %s')
echo "TAG DONE rc=$? $(date -u +%FT%TZ)"
git clone --depth 1 --filter=blob:none --sparse https://github.com/openai/codex.git src-main && (cd src-main && git sparse-checkout set codex-rs && git rev-parse HEAD && git log -1 --format='%H %cI %s')
echo "MAIN DONE rc=$? $(date -u +%FT%TZ)"
