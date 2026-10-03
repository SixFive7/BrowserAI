# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch rig: builds the survival tables from every results\*.jsonl file.
# Writes results\survival.tsv (one row per run) and results\survival-summary.txt.
import json, glob, collections, os, sys

root = r'C:\Source\SixFive7\BrowserAI\.work\hard-kill'
rows = []
for f in sorted(glob.glob(os.path.join(root, 'results', '*.jsonl'))):
    if os.path.basename(f).startswith('smoke'):
        continue
    for line in open(f, encoding='utf-8-sig'):
        line = line.strip()
        if not line:
            continue
        o = json.loads(line)
        o['_file'] = os.path.basename(f)
        rows.append(o)

# the newest successful row per id wins; failed attempts are counted separately
by_id = collections.OrderedDict()
failed = []
for o in rows:
    if o.get('error'):
        failed.append(o)
        continue
    by_id[(o['_file'], o['id'])] = o

def variant(o):
    v = o['path']
    if o.get('warm'): v += '+warm'
    if o.get('fast'): v += '+fast'
    if o.get('extraArgs'): v += '+aggr'
    if o.get('second'): v += '+second%s' % o['second']
    return v

with open(os.path.join(root, 'results', 'survival.tsv'), 'w', encoding='utf-8', newline='\n') as t:
    cols = ['id', 'browser', 'variant', 'kill', 'D', 'actualD', 'cookieHttpOnly', 'cookieJs', 'localStorageKeys', 'indexedDb', 'sessionStorage',
            'B_cookieHttpOnly', 'B_localStorageKeys', 'W0_cookieHttpOnly', 'W0_localStorageKeys', 'processesBeforeKill', 'aliveAfterKill',
            'killToAllExitedMs', 'readerLaunchMs', 'readerNavigateMs', 'integrityOk', 'integrityCount', 'lockAfterKill', 'exitTypeAfterKill',
            'startupIncompleteAfterKill', 'recoveryAfterKill', 'leftTemp', 'leftLocal', 'leftReg', 'startedUtc']
    t.write('\t'.join(cols) + '\n')
    for o in by_id.values():
        s = o.get('survivedA') or {}
        b = o.get('survivedB') or {}
        w = o.get('survivedW0') or {}
        pk = o.get('postKill') or {}
        lock = pk.get('lockfile') if o['browser'] == 'chromium' else pk.get('parentLock')
        prefs = pk.get('preferences')
        exit_type = ''
        if isinstance(prefs, list):
            exit_type = ';'.join(p for p in prefs if p.startswith('exit_type'))
        elif prefs:
            exit_type = str(prefs)
        top = pk.get('topLevel') or []
        integ = o.get('integrity') or {}
        lw = o.get('leftWriter') or {}
        vals = [o['id'], o['browser'], variant(o), o['kill'], o['D'], o.get('actualD'),
                s.get('cookieHttpOnly'), s.get('cookieJs'), s.get('localStorageKeys'), s.get('indexedDb'), s.get('sessionStorage'),
                b.get('cookieHttpOnly', ''), b.get('localStorageKeys', ''), w.get('cookieHttpOnly', ''), w.get('localStorageKeys', ''),
                o.get('processesBeforeKill'), o.get('aliveAfterKill'), o.get('killToAllExitedMs'), o.get('readerLaunchMs', ''), o.get('readerNavigateMs', ''),
                integ.get('ok', ''), integ.get('count', ''), lock, exit_type,
                any(x.startswith('.startup-incomplete') for x in top), ','.join(pk.get('sessionstore') or []) if isinstance(pk.get('sessionstore'), list) else '',
                ';'.join(lw.get('temp') or []), ';'.join(lw.get('local') or []), ';'.join(lw.get('reg') or []), o.get('startedUtc')]
        t.write('\t'.join('' if v is None else str(v) for v in vals) + '\n')

# grouped summary
groups = collections.OrderedDict()
for o in by_id.values():
    key = (o['browser'], variant(o), o['kill'], float(o['D']))
    groups.setdefault(key, []).append(o)

def frac(lst, f):
    n = sum(1 for o in lst if f(o))
    return '%d/%d' % (n, len(lst))

out = []
out.append('browser\tvariant\tkill\tD\truns\tactualD(min-max)\tcookieHttpOnly\tcookieJs\tlocalStorage40\tlocalStorageKeys\tindexedDb\tsessionStorage\tB_cookie\tB_ls40\tW0_cookie\tW0_ls40\tintegrityAllOk')
for key in sorted(groups, key=lambda k: (k[0], k[1], k[2], k[3])):
    lst = groups[key]
    ad = [o.get('actualD') for o in lst if o.get('actualD') is not None]
    lsk = sorted(set((o.get('survivedA') or {}).get('localStorageKeys') for o in lst))
    row = [key[0], key[1], key[2], '%g' % key[3], str(len(lst)),
           '%.3f-%.3f' % (min(ad), max(ad)) if ad else '',
           frac(lst, lambda o: (o.get('survivedA') or {}).get('cookieHttpOnly')),
           frac(lst, lambda o: (o.get('survivedA') or {}).get('cookieJs')),
           frac(lst, lambda o: (o.get('survivedA') or {}).get('localStorageKeys') == 40),
           ','.join(str(x) for x in lsk),
           frac(lst, lambda o: (o.get('survivedA') or {}).get('indexedDb')),
           frac(lst, lambda o: (o.get('survivedA') or {}).get('sessionStorage')),
           frac(lst, lambda o: (o.get('survivedB') or {}).get('cookieHttpOnly')) if any(o.get('survivedB') for o in lst) else '',
           frac(lst, lambda o: (o.get('survivedB') or {}).get('localStorageKeys') == 40) if any(o.get('survivedB') for o in lst) else '',
           frac(lst, lambda o: (o.get('survivedW0') or {}).get('cookieHttpOnly')) if any(o.get('survivedW0') for o in lst) else '',
           frac(lst, lambda o: (o.get('survivedW0') or {}).get('localStorageKeys') == 40) if any(o.get('survivedW0') for o in lst) else '',
           frac(lst, lambda o: (o.get('integrity') or {}).get('ok') == (o.get('integrity') or {}).get('count') and (o.get('integrity') or {}).get('count')) if any(o.get('integrity') for o in lst) else 'n/a']
    out.append('\t'.join(row))
out.append('')
out.append('successful runs: %d; failed attempts (re-run): %d' % (len(by_id), len(failed)))
for o in failed:
    out.append('  FAILED %s: %s' % (o['id'], (o.get('error') or '')[:200]))
open(os.path.join(root, 'results', 'survival-summary.txt'), 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')
print('\n'.join(out))
