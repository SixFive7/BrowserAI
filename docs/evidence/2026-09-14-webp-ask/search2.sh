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
run PW-8  microsoft/playwright 'screenshot empty file'
run PW-9  microsoft/playwright 'VP8 webp maximum dimension'
run PW-10 microsoft/playwright 'full page screenshot too large webp'
run PW-11 microsoft/playwright 'screenshot silently empty no error'
run MCP-8 microsoft/playwright-mcp 'full page screenshot large page'
run MCP-9 microsoft/playwright-mcp 'screenshot returns nothing'
# controls for this batch
run PW-C4  microsoft/playwright 'screenshot fails on large page'
run MCP-C4 microsoft/playwright-mcp 'take screenshot type jpeg'
echo "=== batch2 done ===" >> "$out"
