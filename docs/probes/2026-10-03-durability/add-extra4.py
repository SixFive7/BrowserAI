# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). ss0 (interval 0) came out faster than ss1000 in
# main-fx, so it gets cost runs too. Appended to the work plan, which has not started yet.
import json
p = r'C:\Source\SixFive7\BrowserAI\.work\durability\plans\work.json'
plan = json.load(open(p, encoding='utf-8'))
have = {s['id'] for s in plan}
for rep in (1, 2, 3):
    s = {'id': 'work-fx-ss0-r%d' % rep, 'kind': 'work', 'browser': 'firefox', 'lever': 'ss0', 'rep': rep, 'secs': 60,
         'extraPrefs': {'browser.sessionstore.interval': 0, 'browser.sessionstore.interval.idle': 0}}
    if s['id'] not in have:
        plan.append(s)
json.dump(plan, open(p, 'w', encoding='utf-8'), indent=1)
print(len(plan))
