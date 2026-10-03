# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03; written by the replacement agent). The report table:
# for each browser, lever and store, the age of the store's own write at the kill, split into runs
# that kept it and runs that lost it, pooled over every plan, plus the first on-disk change of the
# store's files after its own write. Usage: python table.py  (reads every survival plan's results)
import json, os, collections
root = r'C:\Source\SixFive7\BrowserAI\.work\durability\results'
import sys
sys.path.insert(0, os.path.dirname(__file__))
from summarize import category
FILES = ['main-cr.jsonl', 'main-fx.jsonl', 'cycle.jsonl', 'idle.jsonl', 'supp.jsonl']
POOL = {'cycle-base': 'base', 'cycle-hide': 'hide', 'cycle-patch': 'patch', 'cycle-ss1000': 'ss1000'}
def rows():
    for f in FILES:
        p = os.path.join(root, f)
        if not os.path.exists(p):
            continue
        for line in open(p, encoding='utf-8-sig'):
            line = line.strip()
            if not line:
                continue
            o = json.loads(line)
            if o.get('error') or o.get('kind') == 'work':
                continue
            o['_file'] = f
            yield o
surv = collections.defaultdict(lambda: {'kept': [], 'lost': []})
first = collections.defaultdict(list)
runs = collections.Counter()
for o in rows():
    b = o['browser']; lv = POOL.get(o.get('lever'), o.get('lever'))
    runs[(b, lv)] += 1
    s = o.get('survived') or {}; ins = o.get('inspectAfterKill') or {}; age = o.get('ageAtKill') or {}
    w = o.get('written') or {}; marks = w.get('marks') or {}; tDone = w.get('tDone')
    checks = [('cookies', age.get('cookie'), bool(s.get('cookieHttpOnly')) and bool(s.get('cookieJs'))),
              ('localStorage', age.get('localStorage'), s.get('localStorageKeys') == 40),
              ('IndexedDB', age.get('indexedDb'), bool(s.get('indexedDb'))),
              ('CacheStorage', age.get('cacheStorage'), bool(s.get('cacheStorage')))]
    if int(o.get('tabs') or 0) > 0:
        want = set(str(k) for k in range(1, int(o['tabs']) + 1))
        got = set(x for x in (s.get('tabsRestored') or '').split(',') if x)
        ondisk = set(t[3:] for t in ins.get('sessionTokens', []) if t.startswith('tab'))
        checks.append(('tabs on disk', age.get('tabs'), want <= ondisk))
        checks.append(('tabs restored', age.get('tabs'), want <= got))
    if b == 'chromium' and w.get('tPref'):
        checks.append(('Preferences', age.get('pref'), ins.get('bookmarkBarShowOnAllTabs') is True))
    for name, a, ok in checks:
        if a is not None:
            surv[(b, lv, name)]['kept' if ok else 'lost'].append(a)
    own = {'cookies': marks.get('cookie'), 'localStorage': marks.get('localStorage'), 'indexedDb': marks.get('indexedDb'),
           'cacheStorage': marks.get('cacheStorage'), 'sessionFile': w.get('tTabs'), 'preferences': w.get('tPref')}
    seen = set()
    for e in o.get('flushEvents', []):
        p = e.split('|')
        if p[1] in ('baseline', 'flush-answered'):
            continue
        cat = category(b, p[1])
        if not cat or cat in seen or own.get(cat) is None or tDone is None:
            continue
        seen.add(cat)
        first[(b, lv, cat)].append((int(p[0]) + tDone - own[cat]) / 1000.0)
print('runs per browser and lever (cycle runs pooled with their lever):')
for k, v in sorted(runs.items()):
    print('  %-9s %-16s %d' % (k[0], k[1], v))
print()
print('%-9s %-16s %-14s %-34s %s' % ('browser', 'lever', 'store', 'kept: n, youngest kept age (s)', 'lost: n, oldest lost age (s)'))
for (b, lv, name), v in sorted(surv.items()):
    k, l = sorted(v['kept']), sorted(v['lost'])
    ks = '%2d kept, youngest %.3f' % (len(k), k[0]) if k else ' 0 kept'
    ls = '%2d lost, oldest %.3f' % (len(l), l[-1]) if l else ' 0 lost'
    flag = '  OVERLAP' if k and l and k[0] < l[-1] else ''
    print('%-9s %-16s %-14s %-34s %s%s' % (b, lv, name, ks, ls, flag))
print()
print('first on-disk change of the store\'s files after its own write, s (runs where it happened before the kill):')
for (b, lv, cat), v in sorted(first.items()):
    v = sorted(v)
    print('  %-9s %-16s %-13s n=%-3d min=%.3f med=%.3f max=%.3f' % (b, lv, cat, len(v), v[0], v[len(v) // 2], v[-1]))
