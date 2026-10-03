# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). Prints the localStorage file events of Firefox runs.
import json, sys, os
root = r'C:\Source\SixFive7\BrowserAI\.work\durability\results'
for f in sys.argv[1:]:
    for line in open(os.path.join(root, f), encoding='utf-8-sig'):
        o = json.loads(line)
        if o.get('browser') != 'firefox' or o.get('error') or o.get('kind') == 'work':
            continue
        ev = [e for e in o.get('flushEvents', []) if os.sep + 'ls' + os.sep in e or 'flush' in e]
        s = o.get('survived') or {}
        print('%-26s actualD=%-6s ls=%-2s %s' % (o['id'], o.get('actualD'), s.get('localStorageKeys'), ev[:10]))
