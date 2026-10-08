# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# For each Claude Code connection to the INSTALLED BrowserAI, the client version the session's own
# transcript records at the time of that connection: the first transcript line at or after the log's
# start, else the last one before it. Usage: python clientver.py <start> <end>, as count.py takes them.
# Read-only: nothing is written anywhere. It prints counts only, never a project folder's name.
import bisect
import collections
import glob
import json
import os
import re
import sys

cache = os.path.join(os.environ['LOCALAPPDATA'], 'claude-cli-nodejs', 'Cache')
projects = os.path.join(os.path.expanduser('~'), '.claude', 'projects')
start, end = sys.argv[1], sys.argv[2]
BACKSLASH = chr(92)
INSTALLED = 'BrowserAI.app' + BACKSLASH * 2 + 'current' + BACKSLASH * 2 + 'BrowserAI.Server.exe'

LINE = re.compile(r'"timestamp":"([^"]+)".*?"version":"(2\.1\.[0-9]+)"|"version":"(2\.1\.[0-9]+)".*?"timestamp":"([^"]+)"')

# The connections in the window, each with its session id and outcome.
wanted = {}
for p in glob.glob(os.path.join(cache, '*', 'mcp-logs-browserai', '*.jsonl')):
    txt = open(p, encoding='utf-8', errors='replace').read()
    m = re.search(r'"timestamp":"([^"]+)"', txt)
    if not m or not (start <= m.group(1) < end) or INSTALLED not in txt:
        continue
    s = re.search(r'"sessionId":"([^"]+)"', txt)
    if 'missing required resultType' in txt:
        kind = 'failed: resultType'
    elif 'Connection established' in txt and 'Failed to fetch tools' not in txt:
        kind = 'worked'
    else:
        kind = 'failed: other'
    wanted[p] = (m.group(1), s.group(1) if s else None, kind)

sessions = {sid for (_, sid, _) in wanted.values() if sid}
timeline = collections.defaultdict(list)
for t in glob.glob(os.path.join(projects, '**', '*.jsonl'), recursive=True):
    sid = os.path.splitext(os.path.basename(t))[0]
    if sid not in sessions:
        continue
    with open(t, encoding='utf-8', errors='replace') as f:
        for line in f:
            m = LINE.search(line)
            if m:
                ts = m.group(1) or m.group(4)
                v = m.group(2) or m.group(3)
                timeline[sid].append((ts, v))
for sid in timeline:
    timeline[sid].sort()

per = collections.Counter()
outcome = collections.Counter()
for p, (t0, sid, kind) in wanted.items():
    v = '?'
    rows = timeline.get(sid) or []
    if rows:
        i = bisect.bisect_left(rows, (t0, ''))
        v = rows[i][1] if i < len(rows) else rows[-1][1]
    per[v] += 1
    outcome[(kind, v)] += 1

print('connections to the installed server:', len(wanted), ' sessions:', len(sessions), ' with a transcript:', len(timeline))
print('by client version:', dict(sorted(per.items())))
for k, n in sorted(outcome.items()):
    print(n, k)
