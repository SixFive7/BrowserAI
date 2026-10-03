# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig: when each store file changed after the child's stdin was closed, per grace run.
import json, re, sys, os
root = r'C:\Source\SixFive7\BrowserAI\.work\hard-kill\results'
pat = re.compile(r'Cookies$|cookies\.sqlite$|leveldb.\d+\.log$|ls.data\.sqlite$')
for name in sys.argv[1:]:
    for l in open(os.path.join(root, name), encoding='utf-8-sig'):
        o = json.loads(l)
        if o.get('error'):
            continue
        t0 = None; out = []
        for e in o['flushEvents']:
            p = e.split('|')
            if p[1] == 'stdin-closed':
                t0 = int(p[0]); continue
            if t0 is None or p[1] == 'baseline':
                continue
            if pat.search(p[1]):
                out.append('%s:+%d(%s)' % (os.path.basename(p[1].replace(chr(92), '/')), int(p[0]) - t0, p[2]))
        s = o['survivedA']
        print('%-26s G=%-5s kill@+%-5s rootExit@+%-5s cookie=%-5s ls=%-2s | %s' % (
            o['id'][:26], o.get('grace', ''), int(round((o.get('graceActual') or 0) * 1000)), o.get('rootExitAfterEofMs'),
            s['cookieHttpOnly'], s['localStorageKeys'], ' '.join(out[:6])))
        if o.get('joinedDuringGrace'):
            print('     joined:', '; '.join(o['joinedDuringGrace'][:6]))
