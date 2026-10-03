# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). Prints, per browser, lever and D, how many runs
# kept each store after the kill, and the first on-disk change of each store's files after the
# write. Usage: python summarize.py <results.jsonl> [<results.jsonl> ...]
import json, re, sys, collections, os

root = r'C:\Source\SixFive7\BrowserAI\.work\durability\results'

def load(files):
    rows = {}
    for f in files:
        p = f if os.path.isabs(f) else os.path.join(root, f)
        for line in open(p, encoding='utf-8-sig'):
            line = line.strip()
            if not line:
                continue
            o = json.loads(line)
            if o.get('error'):
                rows.setdefault('__errors__', []).append(o['id'] + ': ' + o['error'][:200])
                continue
            rows[o['id']] = o
    return rows

def category(browser, path):
    p = path.replace('\\', '/')
    if browser == 'chromium':
        if p.startswith('Default/Network/Cookies'): return 'cookies'
        if p.startswith('Default/Local Storage/'): return 'localStorage'
        if p.startswith('Default/Session Storage/'): return 'sessionStorage'
        if p.startswith('Default/IndexedDB/'): return 'indexedDb'
        if p.startswith('Default/Service Worker/CacheStorage/'): return 'cacheStorage'
        if p.startswith('Default/Sessions/Session_'): return 'sessionFile'
        if p.startswith('Default/Sessions/Tabs_'): return 'tabsFile'
        if p == 'Default/Preferences': return 'preferences'
        return None
    if p.startswith('cookies.sqlite'): return 'cookies'
    if p == 'prefs.js': return 'prefsjs'
    if p.startswith('sessionstore-backups/recovery.jsonlz4'): return 'sessionFile'
    m = re.match(r'storage/default/[^/]+/(ls|idb|cache)/', p)
    if m: return {'ls': 'localStorage', 'idb': 'indexedDb', 'cache': 'cacheStorage'}[m.group(1)]
    return None

def main(files):
    rows = load(files)
    errors = rows.pop('__errors__', [])
    cells = collections.OrderedDict()
    first = collections.defaultdict(list)
    for o in sorted(rows.values(), key=lambda o: (o['browser'], o.get('lever') or '', float(o.get('D') or 0), o['id'])):
        if o.get('kind') == 'work':
            continue
        key = (o['browser'], o.get('lever') or '', o.get('D'))
        c = cells.setdefault(key, collections.Counter())
        s = o.get('survived') or {}
        ins = o.get('inspectAfterKill') or {}
        c['runs'] += 1
        c['cookieHttpOnly'] += bool(s.get('cookieHttpOnly'))
        c['cookieJs'] += bool(s.get('cookieJs'))
        c['localStorage40'] += s.get('localStorageKeys') == 40
        c['localStoragePartial'] += 0 < (s.get('localStorageKeys') or 0) < 40
        c['indexedDb'] += bool(s.get('indexedDb'))
        c['cacheStorage'] += bool(s.get('cacheStorage'))
        want = set(str(k) for k in range(1, int(o.get('tabs') or 0) + 1))
        got = set(x for x in (s.get('tabsRestored') or '').split(',') if x)
        if want:
            c['tabsAllRestored'] += want <= got
            c['tabsSessionStorage'] += want <= set(x for x in (s.get('tabsWithSessionStorage') or '').split(',') if x)
            c['tabsOnDisk'] += want <= set(t[3:] for t in ins.get('sessionTokens', []) if t.startswith('tab'))
        if o['browser'] == 'chromium' and (o.get('written') or {}).get('tPref'):
            c['prefOnDisk'] += ins.get('bookmarkBarShowOnAllTabs') is True
            c['exitCrashedOnDisk'] += ins.get('exitType') == 'Crashed'
        if o.get('cycle'):
            ms = o.get('midSurvival') or {}
            mgot = set(x for x in (ms.get('tabsRestored') or '').split(',') if x)
            c['mid:tabs12Restored'] += want <= mgot
            fgot = got
            c['final:tab9Restored'] += '9' in fgot
            c['final:tabs12Restored'] += want <= fgot
        c.setdefault('actualD', [])
        c['actualD'].append(o.get('actualD'))
        seen = set()
        for e in o.get('flushEvents', []):
            parts = e.split('|')
            if parts[1] in ('baseline', 'flush-answered'):
                continue
            cat = category(o['browser'], parts[1])
            if not cat or cat in seen:
                continue
            seen.add(cat)
            first[(o['browser'], o.get('lever') or '', cat)].append(int(parts[0]))
    cols = ['runs', 'cookieHttpOnly', 'cookieJs', 'localStorage40', 'localStoragePartial', 'indexedDb', 'cacheStorage', 'tabsOnDisk', 'tabsAllRestored', 'tabsSessionStorage', 'prefOnDisk', 'exitCrashedOnDisk', 'mid:tabs12Restored', 'final:tab9Restored', 'final:tabs12Restored']
    print('\t'.join(['browser', 'lever', 'D', 'actualD'] + cols))
    for (b, lv, d), c in cells.items():
        ad = [x for x in c['actualD'] if x is not None]
        rng = '%.3f-%.3f' % (min(ad), max(ad)) if ad else ''
        vals = []
        for k in cols:
            if k == 'runs':
                vals.append(str(c['runs']))
            elif k in c:
                vals.append('%d/%d' % (c[k], c['runs']))
            else:
                vals.append('')
        print('\t'.join([b, lv, str(d), rng] + vals))
    print()
    print('first on-disk change after the write, ms (runs in which it happened before the kill):')
    for (b, lv, cat), v in sorted(first.items()):
        v = sorted(v)
        print('  %-9s %-14s %-15s n=%-3d min=%-6d med=%-6d max=%d' % (b, lv, cat, len(v), v[0], v[len(v) // 2], v[-1]))
    work = [o for o in rows.values() if o.get('kind') == 'work']
    if work:
        print()
        print('cost workload (job I/O over the window, page timer lag):')
        g = collections.defaultdict(list)
        for o in work:
            g[(o['browser'], o.get('lever') or '')].append(o)
        for (b, lv), os_ in sorted(g.items()):
            for o in sorted(os_, key=lambda o: o['id']):
                io = o.get('io') or {}
                pg = o.get('page') or {}
                ev = collections.Counter(category(b, e.split('|')[1]) for e in o.get('events', []))
                print('  %-9s %-14s %-34s win=%5.1fs cpuMs=%s/%s writeMB=%6.2f writeOps=%6d lagP50=%5.1f P99=%6.1f max=%6.1f lsOps=%d flushes=%s flushMs=%s files=%s' % (
                    b, lv, o['id'], (o.get('windowMs') or 0) / 1000.0, io.get('cpuUserMs'), io.get('cpuKernelMs'), (io.get('writeBytes') or 0) / 1e6, io.get('writeOps') or 0,
                    pg.get('lagP50') or 0, pg.get('lagP99') or 0, pg.get('lagMax') or 0, (pg.get('ops') or {}).get('ls', 0),
                    pg.get('flushes'), pg.get('flushMs'), dict((k, v) for k, v in ev.items() if k)))
    if errors:
        print()
        print('errored attempts (re-run):', len(errors))
        for e in errors:
            print('  ' + e)

if __name__ == '__main__':
    main(sys.argv[1:])
