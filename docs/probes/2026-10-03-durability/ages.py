# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). For each store, the age of its own write at the
# moment of the kill (not D, which is measured from the last write), split by whether the store
# survived, and the first on-disk change of the store's files measured from the store's own write.
# Usage: python ages.py <results.jsonl> [...]
import json, os, sys, collections, re

root = r'C:\Source\SixFive7\BrowserAI\.work\durability\results'
sys.path.insert(0, os.path.dirname(__file__))
from summarize import category

def rows(files):
    for f in files:
        for line in open(os.path.join(root, f), encoding='utf-8-sig'):
            line = line.strip()
            if line:
                o = json.loads(line)
                if not o.get('error') and o.get('kind') != 'work':
                    yield o

def main(files):
    surv = collections.defaultdict(lambda: {'kept': [], 'lost': []})
    first = collections.defaultdict(list)
    for o in rows(files):
        b, lv = o['browser'], o.get('lever') or ''
        s = o.get('survived') or {}
        ins = o.get('inspectAfterKill') or {}
        age = o.get('ageAtKill') or {}
        w = o.get('written') or {}
        marks = w.get('marks') or {}
        tDone = w.get('tDone')
        checks = [
            ('cookie', age.get('cookie'), bool(s.get('cookieHttpOnly')) and bool(s.get('cookieJs'))),
            ('localStorage', age.get('localStorage'), s.get('localStorageKeys') == 40),
            ('indexedDb', age.get('indexedDb'), bool(s.get('indexedDb'))),
            ('cacheStorage', age.get('cacheStorage'), bool(s.get('cacheStorage'))),
        ]
        if int(o.get('tabs') or 0) > 0:
            want = set(str(k) for k in range(1, int(o['tabs']) + 1))
            got = set(x for x in (s.get('tabsRestored') or '').split(',') if x)
            ondisk = set(t[3:] for t in ins.get('sessionTokens', []) if t.startswith('tab'))
            checks.append(('tabsOnDisk', age.get('tabs'), want <= ondisk))
            checks.append(('tabsRestored', age.get('tabs'), want <= got))
        if b == 'chromium' and w.get('tPref'):
            checks.append(('prefOnDisk', age.get('pref'), ins.get('bookmarkBarShowOnAllTabs') is True))
        for name, a, ok in checks:
            if a is None:
                continue
            surv[(b, lv, name)]['kept' if ok else 'lost'].append(a)
        # first on-disk change, from each store's own write time
        own = {'cookies': marks.get('cookie'), 'localStorage': marks.get('localStorage'), 'sessionStorage': marks.get('localStorage'),
               'indexedDb': marks.get('indexedDb'), 'cacheStorage': marks.get('cacheStorage'), 'sessionFile': w.get('tTabs'),
               'tabsFile': w.get('tTabs'), 'preferences': w.get('tPref')}
        seen = set()
        for e in o.get('flushEvents', []):
            p = e.split('|')
            if p[1] in ('baseline', 'flush-answered'):
                continue
            cat = category(b, p[1])
            if not cat or cat in seen or own.get(cat) is None or tDone is None:
                continue
            seen.add(cat)
            first[(b, lv, cat)].append(int(p[0]) + tDone - own[cat])
    print('age of each store\'s own write at the kill, seconds: kept (min-max) | lost (min-max)')
    for (b, lv, name), v in sorted(surv.items()):
        k, l = sorted(v['kept']), sorted(v['lost'])
        ks = '%d kept %.3f-%.3f' % (len(k), k[0], k[-1]) if k else '0 kept'
        ls = '%d lost %.3f-%.3f' % (len(l), l[0], l[-1]) if l else '0 lost'
        print('  %-9s %-18s %-13s %-28s | %s' % (b, lv, name, ks, ls))
    print()
    print('first change of the store\'s files after the store\'s own write, ms:')
    for (b, lv, cat), v in sorted(first.items()):
        v = sorted(v)
        print('  %-9s %-18s %-15s n=%-3d min=%-6d med=%-6d max=%d' % (b, lv, cat, len(v), v[0], v[len(v) // 2], v[-1]))

if __name__ == '__main__':
    main(sys.argv[1:])
