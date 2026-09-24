# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# usage: run.sh <mcp-config-basename> <out-basename> [extra claude args...]
set -u
WU=/c/Source/SixFive7/BrowserAI/.work/q254-2026-09-23
CFG="$1"; OUT="$2"; shift 2
export CLAUDE_CONFIG_DIR="$(cygpath -w "$WU/cfg")"
MCPCFG="$(cygpath -w "$WU/rig/$CFG")"
DBG="$(cygpath -w "$WU/out/$OUT.debug.log")"
cd "$WU/proj" || exit 9
echo "=== $(date -Is) START $OUT cfg=$MCPCFG ===" >> "$WU/out/$OUT.meta"
claude -p \
  --mcp-config "$MCPCFG" \
  --strict-mcp-config \
  --model sonnet \
  --tools "" \
  --allowedTools "mcp__probe__ping" \
  --permission-mode acceptEdits \
  --output-format stream-json --verbose \
  --debug-file "$DBG" \
  "$@" \
  > "$WU/out/$OUT.stream.jsonl" 2> "$WU/out/$OUT.stderr.txt" < /dev/null
echo "exit=$?" >> "$WU/out/$OUT.meta"
echo "=== $(date -Is) END $OUT ===" >> "$WU/out/$OUT.meta"
