# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch (Q302): tabulate every run under .work/firefox-launch/runs.
import json, os, re, sys, statistics
from collections import defaultdict

root = os.path.join(os.path.dirname(__file__), '..', 'runs')

def q(xs, p):
    xs = sorted(xs)
    if not xs:
        return None
    k = (len(xs) - 1) * p
    f = int(k)
    c = min(f + 1, len(xs) - 1)
    return xs[f] + (xs[c] - xs[f]) * (k - f)

def markers(log):
    t = open(log, encoding='utf-8', errors='replace').read() if os.path.exists(log) else ''
    return {
        'enableAnswered': bool(re.search(r'RECV \{"id":1[,}]', t)),
        'attached': 'Browser.attachedToTarget' in t,
        'delayedStartupWork': 'limited_access_features' in t,
        'juggler': 'Juggler listening' in t,
    }

runs = sorted(d for d in os.listdir(root) if os.path.isdir(os.path.join(root, d)))
if len(sys.argv) > 1:
    runs = [r for r in runs if r in sys.argv[1:]]
for run in runs:
    rf = os.path.join(root, run, 'results.jsonl')
    if not os.path.exists(rf):
        continue
    rows = [json.loads(l) for l in open(rf)]
    groups = defaultdict(list)
    for r in rows:
        key = 'MOZ_DISABLE_SAFE_MODE_KEY=1' if r.get('safeModeKeyDisabled') else 'no override'
        groups[key].append(r)
    for key, rs in groups.items():
        ok = [r for r in rs if r.get('ok')]
        bad = [r for r in rs if not r.get('ok')]
        lm = [r['launchMs'] for r in ok if r.get('launchMs') is not None]
        safe_bad = sum(1 for r in bad if (r.get('suspectTree') or {}).get('safeModeProcesses'))
        safe_ok = sum(1 for r in ok if (r.get('tree') or {}).get('safeModeProcesses'))
        tree_ok = sum(1 for r in ok if r.get('tree'))
        tree_bad = sum(1 for r in bad if r.get('suspectTree'))
        ms = [markers(os.path.join(root, run, r['id'], 'probe.log')) for r in bad]
        not_enable = sum(1 for m in ms if not m['enableAnswered'])
        print(f"{run:22s} {key:28s} n={len(rs):3d} ok={len(ok):3d} stuck={len(bad):2d}"
              + (f" launch p50={q(lm,.5):.0f} p90={q(lm,.9):.0f} max={max(lm):.0f} ms" if lm else '')
              + f" | safeMode among stuck {safe_bad}/{tree_bad} (tree read), among ok {safe_ok}/{tree_ok}"
              + f" | stuck with Browser.enable unanswered {not_enable}/{len(bad)}")
        for r in bad:
            err = (r.get('error') or '').split('\n')[0][:90]
            print(f"    stuck {r['id']} {r['startUtc'][11:19]}Z files={r.get('profileFiles')} bytes={r.get('profileBytes')} tree={r.get('suspectTree')} err={err}")
