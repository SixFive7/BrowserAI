# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig: first change of each watched store file after the write, per browser.
import json, re, collections, sys, glob, os
root = r'C:\Source\SixFive7\BrowserAI\.work\hard-kill'
files = sys.argv[1:]
rows = {}
for f in files:
    for l in open(os.path.join(root, 'results', f), encoding='utf-8-sig'):
        o = json.loads(l)
        if not o.get('error'):
            rows[o['id']] = o
first = collections.defaultdict(list)
for o in rows.values():
    if o['kill'] == 'clean' or o.get('second'):
        continue
    seen = set()
    for e in o['flushEvents']:
        parts = e.split('|')
        if parts[1] in ('baseline', 'second-write-done'):
            continue
        ms = int(parts[0]); f = parts[1]
        key = re.sub(r'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}', 'GUID', f)
        key = re.sub(r'\d{6}\.log', 'NNNNNN.log', key)
        if key in seen:
            continue
        seen.add(key)
        first[key].append((ms, o['D'], o['id']))
for k, v in sorted(first.items(), key=lambda kv: min(x[0] for x in kv[1])):
    ms = sorted(x[0] for x in v)
    print('%-95s n=%2d first-change ms: min=%d med=%d max=%d' % (k[:95], len(v), ms[0], ms[len(ms) // 2], ms[-1]))
