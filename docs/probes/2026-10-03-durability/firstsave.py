# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03; written by the replacement agent). For every Firefox
# survival run that synchronised on a session write: how long after the first navigation of a
# relaunch that restored a session the first session-file write came, grouped by the session-store
# preferences, with the system idle time at the write (the orchestrator's GetLastInputInfo reading).
import json, os, collections
root = r'C:\Source\SixFive7\BrowserAI\.work\durability\results'
GROUP = {'base': 'default', 'active15': 'default', 'cycle-base': 'default', 'idle1s-default': 'default, idleDelay 1 s',
         'ss1000': 'interval 1000', 'cycle-ss1000': 'interval 1000', 'idle1s-ss1000': 'interval 1000, idleDelay 1 s', 'ss0': 'interval 0'}
g = collections.defaultdict(list)
for f in ['main-fx.jsonl', 'cycle.jsonl', 'idle.jsonl', 'supp.jsonl']:
    for line in open(os.path.join(root, f), encoding='utf-8-sig'):
        o = json.loads(line)
        if o.get('browser') != 'firefox' or o.get('kind') == 'work':
            continue
        w = o.get('written') or {}
        if w.get('primeToSaveMs') is None:
            continue
        g[GROUP.get(o['lever'], o['lever'])].append((w['primeToSaveMs'], bool(w.get('saveSeen')), o['id'], o.get('idleMsAtWrite')))
for k, v in sorted(g.items()):
    seen = sorted(x[0] for x in v if x[1])
    print('%-30s runs=%-3d written within 40 s: %-3d range %s to %s ms' % (k, len(v), len(seen), seen[0] if seen else '-', seen[-1] if seen else '-'))
    for p, s, i, idle in sorted(v, key=lambda x: x[2]):
        print('    %-28s %-6s ms  written=%-5s idle at the write %s ms' % (i, p, s, idle))
