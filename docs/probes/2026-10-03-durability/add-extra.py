# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). Appends the 512-operation cookie cells to the
# idle plan before that plan starts (it runs after cycle in the chain).
import json, os
p = r'C:\Source\SixFive7\BrowserAI\.work\durability\plans\idle.json'
plan = json.load(open(p, encoding='utf-8'))
have = {s['id'] for s in plan}
for d in (0.25,):
    for rep in (1, 2, 3):
        s = {'id': 'cr-batch512-D%s-r%d' % (d, rep), 'browser': 'chromium', 'lever': 'batch512', 'D': d, 'rep': rep,
             'warm': True, 'preIdleMs': 12000, 'tabs': 2, 'prefsChange': True, 'flush': 'batch512'}
        if s['id'] not in have:
            plan.append(s)
json.dump(plan, open(p, 'w', encoding='utf-8'), indent=1)
print(len(plan), [s['id'] for s in plan])
