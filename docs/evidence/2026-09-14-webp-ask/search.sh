# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# Duplicate search with positive controls. Every query is recorded with its hit
# count; a zero-result query is only believed when a control query through the
# SAME path returns hits.
set -u
out="search-results.txt"
: > "$out"
run() {
  local label="$1"; shift
  local repo="$1"; shift
  local q="$*"
  echo "=== [$label] repo:$repo  q=<$q>" >> "$out"
  local json
  json=$(gh search issues --repo "$repo" --include-prs --limit 25 --json number,title,state,url,createdAt -- "$q" 2>&1)
  if [ $? -ne 0 ]; then echo "  ERROR: $json" >> "$out"; echo "$label: ERROR"; return; fi
  local n
  n=$(printf '%s' "$json" | python -c "import json,sys; d=json.load(sys.stdin); print(len(d))" 2>/dev/null || echo "?")
  echo "  hits=$n" >> "$out"
  printf '%s' "$json" | python -c "
import json,sys
for i in json.load(sys.stdin):
    print('   %-7s %-7s %s  %s' % (i['number'], i['state'], i['createdAt'][:10], i['title'][:88]))
" >> "$out" 2>/dev/null
  echo "$label: hits=$n"
}

# --- microsoft/playwright-mcp : the defect ---
run MCP-1 microsoft/playwright-mcp 'webp screenshot empty'
run MCP-2 microsoft/playwright-mcp '16383'
run MCP-3 microsoft/playwright-mcp '16384'
run MCP-4 microsoft/playwright-mcp 'webp'
run MCP-5 microsoft/playwright-mcp 'zero-byte screenshot'
run MCP-6 microsoft/playwright-mcp 'empty image screenshot'
run MCP-7 microsoft/playwright-mcp 'fullPage dimension limit'
# --- microsoft/playwright-mcp : positive controls ---
run MCP-C1 microsoft/playwright-mcp 'screenshot'
run MCP-C2 microsoft/playwright-mcp 'browser_take_screenshot'
run MCP-C3 microsoft/playwright-mcp 'absolute paths tool result links'

# --- microsoft/playwright : the defect ---
run PW-1 microsoft/playwright 'webp screenshot empty'
run PW-2 microsoft/playwright '16383'
run PW-3 microsoft/playwright '16384 screenshot'
run PW-4 microsoft/playwright 'webp dimension limit'
run PW-5 microsoft/playwright 'webp fullPage'
run PW-6 microsoft/playwright 'screenshot zero bytes'
run PW-7 microsoft/playwright 'webp empty image'
# --- microsoft/playwright : positive controls ---
run PW-C1 microsoft/playwright 'webp screenshot'
run PW-C2 microsoft/playwright '[MCP] Option for absolute paths in tool result links'
run PW-C3 microsoft/playwright 'fullPage screenshot'
echo "=== done ===" >> search-results.txt
