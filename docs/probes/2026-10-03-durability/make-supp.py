# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03; written by the replacement agent). The supplementary
# plan: three survival cells that close brackets the first plans left open, and the form-and-scroll
# cost workload for the Firefox session-store lever. Three runs per cell, shuffled with a fixed seed.
import json, random, os
root = r'C:\Source\SixFive7\BrowserAI\.work\durability\plans'
FX = dict(warm=True, syncToSave=True, tabs=2)
SS1000 = {'browser.sessionstore.interval': 1000, 'browser.sessionstore.interval.idle': 1000}
SS0 = {'browser.sessionstore.interval': 0, 'browser.sessionstore.interval.idle': 0}
plan = []
def cell(id_, lever, d, reps, **kw):
    for rep in reps:
        s = {'id': '%s-r%d' % (id_, rep), 'browser': 'firefox', 'lever': lever, 'D': d, 'rep': rep}
        s.update(kw)
        plan.append(s)
# Default prefs, D past the save the first plans saw at 15.8 to 17.7 s after the write.
cell('fx-base-D22', 'base', 22, (1, 2, 3), **FX)
# Forced idle regime again (idleDelay 1 s), runs 4 to 6, so the cell has three runs with no input.
cell('fx-idle1s-default-D20', 'idle1s-default', 20, (4, 5, 6), extraPrefs={'browser.sessionstore.idleDelay': 1}, **FX)
# The lower bracket for interval 0, which the first plans kept at 1.75 s and never lost.
cell('fx-ss0-D1', 'ss0', 1, (1, 2, 3), extraPrefs=SS0, **FX)
for lever, prefs in (('base', None), ('ss1000', SS1000), ('ss0', SS0)):
    for rep in (1, 2, 3):
        s = {'id': 'form-fx-%s-r%d' % (lever, rep), 'kind': 'work', 'page': 'form', 'browser': 'firefox', 'lever': lever, 'rep': rep, 'secs': 60}
        if prefs: s['extraPrefs'] = prefs
        plan.append(s)
random.Random(20261003 + 1).shuffle(plan)
with open(os.path.join(root, 'supp.json'), 'w', encoding='utf-8') as f:
    json.dump(plan, f, indent=1)
print(len(plan), [s['id'] for s in plan])
