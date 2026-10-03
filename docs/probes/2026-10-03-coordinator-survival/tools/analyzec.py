# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Lane c survival probe, 2026-10-03: one row per run, and a per-scenario summary.
# usage: python analyzec.py <runs\batch dir>
import json, os, sys, collections

root = sys.argv[1]
rows = []
for name in sorted(os.listdir(root)):
    rj = os.path.join(root, name, 'result.json')
    if not os.path.isfile(rj):
        continue
    d = json.load(open(rj, encoding='utf-8-sig'))
    procs = d.get('procs', [])
    def first(role):
        for p in procs:
            if p.get('role') == role:
                return p
        return None
    server = first('server')
    standin = first('standin')
    killers = [p for p in procs if p.get('role', '').startswith('KILLER')]
    host = d.get('hostBrowser') or {}
    scen = name.rsplit('-r', 1)[0]
    rows.append({
        'run': name,
        'scenario': scen,
        'clientExit': d.get('clientExitCode'),
        'waitTimeout': d.get('waitTimeout') or '',
        'serverExit': server and server.get('exitCode'),
        'inTreeStandinExit': standin and standin.get('exitCode'),
        'taskkillSeen': len(killers),
        'stillAliveAfterSettle': ','.join(d.get('stillAliveAfterSettle') or []),
        'hostBrowserAfterSettle': host.get('afterSettle', host.get('error', '')),
        'hostBrowserAfter3sMore': host.get('after3sMore', ''),
        'release': host.get('release', ''),
        'afterRelease': host.get('afterRelease', ''),
    })

cols = list(rows[0].keys()) if rows else []
out = os.path.join(root, 'analysis-survival.tsv')
with open(out, 'w', encoding='utf-8', newline='\n') as f:
    f.write('\t'.join(cols) + '\n')
    for r in rows:
        f.write('\t'.join('' if r[c] is None else str(r[c]) for c in cols) + '\n')

by = collections.OrderedDict()
for r in rows:
    by.setdefault(r['scenario'], []).append(r)
summary = os.path.join(root, 'summary-survival.tsv')
with open(summary, 'w', encoding='utf-8', newline='\n') as f:
    f.write('scenario\truns\tserver killed (exit!=77)\tin-tree stand-in dead\thost browser alive after settle\talive 3 s later\treleased and died\n')
    for scen, rs in by.items():
        n = len(rs)
        killed = sum(1 for r in rs if r['serverExit'] is not None and r['serverExit'] != 77)
        tree = sum(1 for r in rs if r['inTreeStandinExit'] is not None)
        alive = sum(1 for r in rs if str(r['hostBrowserAfterSettle']).startswith('alive'))
        alive2 = sum(1 for r in rs if str(r['hostBrowserAfter3sMore']).startswith('alive'))
        rel = sum(1 for r in rs if 'diedWithin10s=True' in str(r['release']) and str(r['afterRelease']).startswith('exited'))
        f.write(f'{scen}\t{n}\t{killed}/{n}\t{tree}/{n}\t{alive}/{n}\t{alive2}/{n}\t{rel}/{n}\n')
print(open(summary, encoding='utf-8').read())
