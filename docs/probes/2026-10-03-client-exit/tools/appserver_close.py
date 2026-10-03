# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

"""For Codex app-server runs ended by closing stdin: server death and app-server exit relative to the close."""
import json, glob, os, statistics, datetime, sys

S = r'C:\Source\SixFive7\BrowserAI\.work\client-exit'

def utc(s):
    s = s.rstrip('Z')
    if '+' in s[10:]:
        s = s[:s.rindex('+')]
    head, _, frac = s.partition('.')
    frac = (frac + '000000')[:6]
    return datetime.datetime.fromisoformat(f'{head}.{frac}')

out = []
for batch in ['cx', 'cxnew', 'keeper']:
    vals_s, vals_c = [], []
    for d in sorted(glob.glob(os.path.join(S, 'runs', batch, '*d2-*'))):
        close = None
        with open(os.path.join(d, 'harness.log'), encoding='utf-8') as f:
            for line in f:
                p = line.rstrip('\n').split('\t')
                if len(p) > 5 and p[4] == 'ACTION_CLOSE_STDIN' and 'why=step' in p[5]:
                    close = utc(p[1])
        res = json.load(open(os.path.join(d, 'result.json'), encoding='utf-8'))
        srv = next((p for p in res['procs'] if p['role'] == 'server'), None)
        cli = next((p for p in res['procs'] if p['role'] == 'client'), None)
        if close and srv and srv.get('exitUtc'):
            vals_s.append((utc(srv['exitUtc']) - close).total_seconds() * 1000)
            vals_c.append((utc(cli['exitUtc']) - close).total_seconds() * 1000)
    if vals_s:
        out.append(f"{batch}/d2: server death after app-server stdin close {min(vals_s):.1f}..{max(vals_s):.1f} (median {statistics.median(vals_s):.1f}, n={len(vals_s)}); app-server exit after close {min(vals_c):.1f}..{max(vals_c):.1f} (median {statistics.median(vals_c):.1f})")
print('\n'.join(out))
with open(os.path.join(S, 'ranges.txt'), 'a', encoding='utf-8') as f:
    f.write('\n'.join(out) + '\n')
