# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
set -u
out="search-terms.txt"
q() {
  local label="$1"; shift
  local query="$*"
  echo "=== [$label] q=<$query>" >> "$out"
  local json
  json=$(gh api -X GET search/issues -f q="$query" -f per_page=25 2>&1)
  if ! printf '%s' "$json" | head -c 1 | grep -q '{'; then echo "  ERROR: $json" >> "$out"; echo "$label: ERROR"; return; fi
  printf '%s' "$json" | python -c "
import json,sys
d=json.load(sys.stdin)
print('  total_count=%d' % d['total_count'])
for i in d['items'][:25]:
    kind='PR ' if 'pull_request' in i else 'ISS'
    print('   %s %-7s %-7s %s %-16s %s' % (kind, i['number'], i['state'], i['created_at'][:10], i['user']['login'][:16], i['title'][:80]))
" >> "$out"
  local t
  t=$(printf '%s' "$json" | python -c "import json,sys; print(json.load(sys.stdin)['total_count'])")
  echo "$label: total=$t"
  sleep 2
}
q M1 'repo:microsoft/playwright-mcp webp screenshot empty'
q M2 'repo:microsoft/playwright-mcp 16383'
q M3 'repo:microsoft/playwright-mcp 16384'
q M4 'repo:microsoft/playwright-mcp screenshot empty'
q M5 'repo:microsoft/playwright-mcp webp fullPage'
q P1 'repo:microsoft/playwright webp screenshot empty'
q P2 'repo:microsoft/playwright webp 16383'
q P3 'repo:microsoft/playwright 16384 screenshot'
q P4 'repo:microsoft/playwright webp dimension'
q P5 'repo:microsoft/playwright webp fullPage'
q P6 'repo:microsoft/playwright screenshot empty webp'
q P7 'repo:microsoft/playwright screenshot zero bytes'
q P8 'repo:microsoft/playwright webp limit height'
echo "=== batch5 done ===" >> "$out"
