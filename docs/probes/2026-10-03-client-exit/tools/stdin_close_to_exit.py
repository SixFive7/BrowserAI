# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

"""For stream-json runs ended by closing claude's stdin: claude's exit relative to the close."""
import json, glob, os, statistics, datetime

S = r'C:\Source\SixFive7\BrowserAI\.work\client-exit'

def utc(s):
    s = s.rstrip('Z')
    if '+' in s[10:]:
        s = s[:s.rindex('+')]
    head, _, frac = s.partition('.')
    return datetime.datetime.fromisoformat(f"{head}.{(frac + '000000')[:6]}")

vals = []
for pat in [r'runs\cc\b1-*', r'runs\cc\c1-*', r'runs\cc2\b1-*', r'runs\cc2\c1-*', r'runs\extra\e4-*']:
    for d in sorted(glob.glob(os.path.join(S, pat))):
        close = None
        for line in open(os.path.join(d, 'harness.log'), encoding='utf-8'):
            p = line.rstrip('\n').split('\t')
            if len(p) > 5 and p[4] == 'ACTION_CLOSE_STDIN' and 'why=step' in p[5]:
                close = utc(p[1])
        res = json.load(open(os.path.join(d, 'result.json'), encoding='utf-8'))
        cli = next(p for p in res['procs'] if p['role'] == 'client')
        if close and cli.get('exitUtc'):
            vals.append((utc(cli['exitUtc']) - close).total_seconds() * 1000)
vals.sort()
line = f"claude exit after its stdin was closed (b1, c1, e4; n={len(vals)}): {vals[0]:.1f}..{vals[-1]:.1f} ms, median {statistics.median(vals):.1f}"
print(line)
with open(os.path.join(S, 'ranges.txt'), 'a', encoding='utf-8') as f:
    f.write(line + '\n')
