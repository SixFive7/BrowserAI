# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

#!/usr/bin/env bash
# Term-AND search through the REST search API, where the q string is under our
# control. Every zero is paired with a control proven to match through the very
# same path and query shape.
set -u
out="search-terms.txt"
: > "$out"
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
# --- CONTROLS FIRST: prove the term-AND shape can match known targets ---
q C1 'repo:microsoft/playwright webp transparency lossless detection'
q C2 'repo:microsoft/playwright-mcp screenshot explicit filename cwd'
q C3 'repo:microsoft/playwright-mcp webp'
q C4 'repo:microsoft/playwright screenshot webp'
