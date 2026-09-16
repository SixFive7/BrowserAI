# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
set -u
out="search-results.txt"
run() {
  local label="$1"; shift
  local repo="$1"; shift
  local q="$*"
  echo "=== [$label] repo:$repo  q=<$q>" >> "$out"
  local json
  json=$(gh search issues --repo "$repo" --include-prs --limit 25 --json number,title,state,url,createdAt -- "$q" 2>&1)
  if [ $? -ne 0 ]; then echo "  ERROR: $json" >> "$out"; echo "$label: ERROR"; return; fi
  local n
  n=$(printf '%s' "$json" | python -c "import json,sys; print(len(json.load(sys.stdin)))" 2>/dev/null || echo "?")
  echo "  hits=$n" >> "$out"
  printf '%s' "$json" | python -c "
import json,sys
for i in json.load(sys.stdin):
    print('   %-7s %-7s %s  %s' % (i['number'], i['state'], i['createdAt'][:10], i['title'][:88]))
" >> "$out" 2>/dev/null
  echo "$label: hits=$n"
}
# Multi-word AND controls, each with a known target, proving batch-2 shapes can match.
run MCP-C5 microsoft/playwright-mcp 'screenshot explicit filename server cwd'
run PW-C5  microsoft/playwright 'webp transparency lossless detection'
run PW-C6  microsoft/playwright 'screenshot fullPage large'
echo "=== batch3 done ===" >> "$out"
