# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). Adds the active-regime Firefox cost runs (the
# default 15 s interval with idleDelay pushed out to a day) to the work plan, which has not
# started yet, so the session-store lever is compared against the active regime and not only
# against the one-hour idle regime the machine happens to be in.
import json
p = r'C:\Source\SixFive7\BrowserAI\.work\durability\plans\work.json'
plan = json.load(open(p, encoding='utf-8'))
have = {s['id'] for s in plan}
for rep in (1, 2, 3):
    s = {'id': 'work-fx-active15-r%d' % rep, 'kind': 'work', 'browser': 'firefox', 'lever': 'active15', 'rep': rep, 'secs': 60,
         'extraPrefs': {'browser.sessionstore.idleDelay': 86400}}
    if s['id'] not in have:
        plan.append(s)
json.dump(plan, open(p, 'w', encoding='utf-8'), indent=1)
print(len(plan), [s['id'] for s in plan])
