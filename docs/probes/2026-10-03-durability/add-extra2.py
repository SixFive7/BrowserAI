# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). The machine went idle (no keyboard or mouse input
# for more than 180 s) during the Firefox plan, which puts Firefox's session saver on its
# one-hour idle interval. These cells pin the ACTIVE regime by pushing idleDelay out to a day,
# so the 15 s interval is measured whether or not anyone is at the machine. Appended to the idle
# plan, which has not started yet.
import json
p = r'C:\Source\SixFive7\BrowserAI\.work\durability\plans\idle.json'
plan = json.load(open(p, encoding='utf-8'))
have = {s['id'] for s in plan}
for d in (13, 17):
    for rep in (1, 2, 3):
        s = {'id': 'fx-active15-D%s-r%d' % (d, rep), 'browser': 'firefox', 'lever': 'active15', 'D': d, 'rep': rep,
             'warm': True, 'syncToSave': True, 'tabs': 2, 'extraPrefs': {'browser.sessionstore.idleDelay': 86400}}
        if s['id'] not in have:
            plan.append(s)
json.dump(plan, open(p, 'w', encoding='utf-8'), indent=1)
print(len(plan), [s['id'] for s in plan])
