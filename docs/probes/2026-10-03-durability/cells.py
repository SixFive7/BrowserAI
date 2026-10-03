# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03; written by the replacement agent). Lists every
# measured cell (plan, browser, lever, D or workload) with its run count, its error count, and who
# ran it: the plans before supp were planned and started by agent a3f0c8f85ec6ad79f (its chain ran
# on to 15:43:34Z after the agent was stopped at 15:07:14Z); supp was run by its replacement.
import json, os, collections
root = r'C:\Source\SixFive7\BrowserAI\.work\durability\results'
WHO = {'main-cr': 'predecessor', 'main-fx': 'predecessor', 'cycle': 'predecessor',
       'idle': 'predecessor (ran after it stopped)', 'work': 'predecessor (ran after it stopped)', 'supp': 'replacement'}
for plan in ['main-cr', 'main-fx', 'cycle', 'idle', 'work', 'supp']:
    p = os.path.join(root, plan + '.jsonl')
    if not os.path.exists(p):
        continue
    c = collections.OrderedDict()
    for line in open(p, encoding='utf-8-sig'):
        o = json.loads(line)
        if o.get('kind') == 'work':
            key = (o['browser'], o['lever'], 'work:' + (o.get('pageKind') or 'work'))
        else:
            key = (o['browser'], o['lever'], 'D=%s%s' % (o.get('D'), (' +cycle %s' % o['cycle']) if o.get('cycle') else ''))
        e = c.setdefault(key, [0, 0])
        e[0] += 1
        e[1] += 1 if o.get('error') else 0
    print('== %s (%s): %d runs in %d cells' % (plan, WHO[plan], sum(v[0] for v in c.values()), len(c)))
    for (b, lv, d), (n, err) in sorted(c.items()):
        print('   %-9s %-16s %-18s runs=%d errors=%d' % (b, lv, d, n, err))
