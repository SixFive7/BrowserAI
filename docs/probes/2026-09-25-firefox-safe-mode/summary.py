# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch (Q302): pooled launch statistics across the runs.
import json, os

root = os.path.join(os.path.dirname(__file__), '..', 'runs')

def load(run):
    p = os.path.join(root, run, 'results.jsonl')
    return [json.loads(l) for l in open(p)] if os.path.exists(p) else []

def q(xs, p):
    xs = sorted(xs)
    k = (len(xs) - 1) * p
    f = int(k)
    c = min(f + 1, len(xs) - 1)
    return xs[f] + (xs[c] - xs[f]) * (k - f)

def line(label, rows):
    ok = [r for r in rows if r.get('ok')]
    bad = [r for r in rows if not r.get('ok')]
    lm = [r['launchMs'] for r in ok if r.get('launchMs') is not None]
    nm = [r['navMs'] for r in ok if r.get('navMs') is not None]
    sm_bad = sum(1 for r in bad if (r.get('suspectTree') or {}).get('safeModeProcesses'))
    read_bad = sum(1 for r in bad if r.get('suspectTree'))
    sm_ok = sum(1 for r in ok if (r.get('tree') or {}).get('safeModeProcesses'))
    read_ok = sum(1 for r in ok if r.get('tree'))
    s = f"{label:58s} n={len(rows):3d} stuck={len(bad):2d}"
    if lm:
        s += f" | launch ms min {min(lm):.0f} p50 {q(lm,.5):.0f} p90 {q(lm,.9):.0f} max {max(lm):.0f}"
    if nm:
        s += f" | nav ms p50 {q(nm,.5):.0f} max {max(nm):.0f}"
    s += f" | -safeMode: stuck {sm_bad}/{read_bad} read, healthy {sm_ok}/{read_ok} read"
    print(s)

ab1 = load('ab1'); ab0 = load('abtest0')
ctl = [r for r in ab1 + ab0 if not r.get('safeModeKeyDisabled')]
trt = [r for r in ab1 + ab0 if r.get('safeModeKeyDisabled')]
line('serial, no override: smoke + serial1 + A/B control', load('smoke') + load('serial1') + ctl)
line('  of which A/B control arm (interleaved)', ctl)
line('serial, MOZ_DISABLE_SAFE_MODE_KEY=1 (A/B treatment arm)', trt)
line('batches of 3, no override', load('par3'))
line('batches of 3, MOZ_DISABLE_SAFE_MODE_KEY=1', load('par3k'))
line('batches of 6, MOZ_DISABLE_SAFE_MODE_KEY=1', load('par6k'))
line('forced: MOZ_SAFE_MODE_RESTART=1', load('forced1'))
line('forced: MOZ_SAFE_MODE_RESTART=1 + MOZ_DISABLE_SAFE_MODE_KEY=1', load('forced2'))
line('job commit limit 450 MB, override on', load('mem450'))
line('job commit limit 300 MB, override on', load('mem300'))
line('job CPU cap 1% of machine (~0.32 CPU), override on', load('cpu100'))
