# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03). Writes the measurement plans. Three runs per cell.
# Order is shuffled with a fixed seed so a slow minute on the machine spreads over many cells.
import json, random, os

root = r'C:\Source\SixFive7\BrowserAI\.work\durability\plans'
REPS = 3
AGGR = '--enable-aggressive-domstorage-flushing'
HIDE = '--hide-crash-restore-bubble'

def cells(browser, lever, ds, **kw):
    out = []
    for d in ds:
        for rep in range(1, REPS + 1):
            s = {'id': '%s-%s-D%s-r%d' % ('cr' if browser == 'chromium' else 'fx', lever, d, rep), 'browser': browser, 'lever': lever, 'D': d, 'rep': rep}
            s.update(kw)
            out.append(s)
    return out

CR = dict(warm=True, preIdleMs=12000, tabs=2, prefsChange=True)
FX = dict(warm=True, syncToSave=True, tabs=2)

main_cr = []
main_cr += cells('chromium', 'base', [0.5, 1.5, 3, 6, 9, 11, 29, 31], **CR)
main_cr += cells('chromium', 'noaggr', [0.5, 1.5, 3, 6], dropArgs=[AGGR], **CR)
main_cr += cells('chromium', 'hide', [1, 1.5, 2, 2.5, 3.5], extraArgs=[HIDE], **CR)
main_cr += cells('chromium', 'patch', [1.5, 3.5], patchExitType=True, **CR)
main_cr += cells('chromium', 'cdpflush', [0, 0.1, 0.25, 0.5], flush='cdp-clearorigin', **CR)
main_cr += cells('chromium', 'cdpsetdel', [0, 0.25], flush='cdp-setdel', **CR)
main_cr += cells('chromium', 'pageclose-noaggr', [0.2], flush='pageclose', dropArgs=[AGGR], warm=True, preIdleMs=12000, tabs=0)
main_cr += cells('chromium', 'noclose-noaggr', [0.2], dropArgs=[AGGR], warm=True, preIdleMs=12000, tabs=0)

main_fx = []
main_fx += cells('firefox', 'base', [0.5, 3, 4.5, 6, 13, 16.5], **FX)
SS1000 = {'browser.sessionstore.interval': 1000, 'browser.sessionstore.interval.idle': 1000}
main_fx += cells('firefox', 'ss1000', [1, 1.75, 2.5, 3.5], extraPrefs=SS1000, **FX)
main_fx += cells('firefox', 'ss0', [1.75, 2.5], extraPrefs={'browser.sessionstore.interval': 0, 'browser.sessionstore.interval.idle': 0}, **FX)
main_fx += cells('firefox', 'pageclose', [0.2], flush='pageclose', warm=True, tabs=0)
main_fx += cells('firefox', 'noclose', [0.2], warm=True, tabs=0)

cycle = []
cycle += cells('chromium', 'cycle-base', [6], cycle=6, **CR)
cycle += cells('chromium', 'cycle-hide', [6], cycle=6, extraArgs=[HIDE], **CR)
cycle += cells('chromium', 'cycle-patch', [6], cycle=6, patchExitType=True, **CR)
cycle += cells('firefox', 'cycle-base', [17], cycle=17, **FX)
cycle += cells('firefox', 'cycle-ss1000', [4], cycle=4, extraPrefs=SS1000, **FX)

# Forced idle: Firefox treats the user as idle after browser.sessionstore.idleDelay seconds without
# input anywhere on the machine, and then saves at most every browser.sessionstore.interval.idle ms.
idle = []
idle += cells('firefox', 'idle1s-default', [20], extraPrefs={'browser.sessionstore.idleDelay': 1}, **FX)
idle += cells('firefox', 'idle1s-ss1000', [3.5], extraPrefs={'browser.sessionstore.idleDelay': 1, **SS1000}, **FX)

work = []
for browser, lever, kw in [
    ('chromium', 'base', {}),
    ('chromium', 'noaggr', {'dropArgs': [AGGR]}),
    ('chromium', 'cdpflush1s', {'flushKind': 'cdp-clearorigin', 'flushEveryMs': 1000}),
    ('firefox', 'base', {}),
    ('firefox', 'ss1000', {'extraPrefs': SS1000}),
]:
    for rep in range(1, REPS + 1):
        s = {'id': 'work-%s-%s-r%d' % ('cr' if browser == 'chromium' else 'fx', lever, rep), 'kind': 'work', 'browser': browser, 'lever': lever, 'rep': rep, 'secs': 60}
        s.update(kw)
        work.append(s)

rnd = random.Random(20261003)
for name, plan in [('main-cr', main_cr), ('main-fx', main_fx), ('cycle', cycle), ('idle', idle), ('work', work)]:
    rnd.shuffle(plan)
    with open(os.path.join(root, name + '.json'), 'w', encoding='utf-8') as f:
        json.dump(plan, f, indent=1)
    print(name, len(plan))
