# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Counts Claude Code's connections to the INSTALLED BrowserAI, read from the client's own MCP logs.
# Usage: python count.py <start> <end>, both ISO-8601 UTC strings compared with each log's first timestamp.
# Read-only: every log is opened for reading and nothing is written anywhere. It prints counts only and
# never a project folder's name, because those folders name the person's own projects.
import collections
import glob
import json
import os
import sys

BACKSLASH = chr(92)
# The installed server's image, as the client writes it into its log: JSON-escaped, two backslashes.
INSTALLED = 'BrowserAI.app' + BACKSLASH * 2 + 'current' + BACKSLASH * 2 + 'BrowserAI.Server.exe'

root = os.path.join(os.environ['LOCALAPPDATA'], 'claude-cli-nodejs', 'Cache')
start, end = sys.argv[1], sys.argv[2]

rows = []
for path in glob.glob(os.path.join(root, '*', 'mcp-logs-browserai', '*.jsonl')):
    raw = open(path, encoding='utf-8', errors='replace').read()
    first = None
    version = era = negotiated = None
    established = result_type = fetch_failed = False
    for line in raw.splitlines():
        try:
            record = json.loads(line)
        except ValueError:
            continue
        said = record.get('debug') or record.get('error') or ''
        if first is None and record.get('timestamp'):
            first = record['timestamp']
        if 'Connection established with capabilities:' in said:
            established = True
            try:
                capabilities = json.loads(said.split('capabilities:', 1)[1])
                version = (capabilities.get('serverVersion') or {}).get('version')
                era = capabilities.get('protocolEra')
                negotiated = capabilities.get('negotiatedProtocolVersion')
            except ValueError:
                pass
        if 'missing required resultType' in said:
            result_type = True
        if 'Failed to fetch tools' in said:
            fetch_failed = True
    if first is None or not (start <= first < end) or INSTALLED not in raw:
        continue
    if result_type:
        outcome = 'failed: resultType'
    elif fetch_failed or not established:
        outcome = 'failed: other'
    else:
        outcome = 'worked'
    rows.append((first, os.path.dirname(os.path.dirname(path)), outcome, version, era, negotiated))

print('connections to the installed server:', len(rows))
print('project folders:', len({row[1] for row in rows}))
if rows:
    print('first and last start:', min(row[0] for row in rows), max(row[0] for row in rows))
for key, count in sorted(collections.Counter(row[2:] for row in rows).items(), key=lambda item: str(item[0])):
    print(count, key)
