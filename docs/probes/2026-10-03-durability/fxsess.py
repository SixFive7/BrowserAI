# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03; written by the replacement agent). Per Firefox run:
# the system idle time at the write and at the kill, the prime-to-save time, the tab-to-done gap,
# and every session-store file event after the write (ms from tDone).
import json, sys, os
root = r'C:\Source\SixFive7\BrowserAI\.work\durability\results'
for f in sys.argv[1:]:
    for line in open(os.path.join(root, f), encoding='utf-8-sig'):
        o = json.loads(line)
        if o.get('browser') != 'firefox' or o.get('kind') == 'work':
            continue
        w = o.get('written') or {}
        m = w.get('marks') or {}
        tabsGap = (w.get('tDone') - w.get('tTabs')) if w.get('tTabs') and w.get('tDone') else None
        ev = [e for e in o.get('flushEvents', []) if 'sessionstore' in e]
        s = o.get('survived') or {}
        ins = o.get('inspectAfterKill') or {}
        print('%-28s %-8s idleW=%-7s idleK=%-7s prime=%-5s tabs->done=%-5s tabsRestored=%-6s onDisk=%s ev=%s' % (
            o['id'], o.get('startedUtc', '')[11:19], o.get('idleMsAtWrite'), o.get('idleMsAtKill'), m.get('primeToSaveMs') or w.get('primeToSaveMs'),
            tabsGap, s.get('tabsRestored'), ','.join(ins.get('sessionTokens', [])), ev))
