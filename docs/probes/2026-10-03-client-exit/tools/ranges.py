# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

"""Timing ranges for the abrupt-kill and Codex scenarios, read from the analysis TSVs."""
import csv, statistics

S = r'C:\Source\SixFive7\BrowserAI\.work\client-exit'

def load(b):
    with open(rf'{S}\analysis-{b}.tsv', encoding='utf-8') as f:
        for r in csv.DictReader(f, delimiter='\t'):
            r['batch'] = b
            r['sc'] = r['run'].split('-')[0]
            yield r

def num(x):
    try:
        return float(x)
    except (TypeError, ValueError):
        return None

def rng(vals):
    v = sorted(x for x in vals if x is not None)
    if not v:
        return '-'
    return f'{v[0]:.1f}..{v[-1]:.1f} (median {statistics.median(v):.1f}, n={len(v)})'

rows = []
for b in ['cc', 'cc2', 'cx', 'cxnew', 'extra', 'vsc', 'tui']:
    rows.extend(r for r in load(b) if '-d' in r['run'])

def sel(sc, batch=None):
    return [r for r in rows if r['sc'] == sc and (batch is None or r['batch'] == batch)]

lines = []
for sc in ['b2', 'c2']:
    rs = sel(sc)
    lines.append(f"{sc}: server death after claude death {rng(num(r['server_exit_vs_client_exit_ms']) for r in rs)}; stand-in after server {rng(num(r['standin_minus_server_exit_ms']) for r in rs)}")
rs = sel('e5')
lines.append(f"e5: claude death after host exit {rng((num(r['server_exit_vs_client_exit_ms']) - num(r['server_exit_vs_hosted_claude_exit_ms'])) for r in rs)}; server death after claude death {rng(num(r['server_exit_vs_hosted_claude_exit_ms']) for r in rs)}; server death after host exit {rng(num(r['server_exit_vs_client_exit_ms']) for r in rs)}; stand-in after server {rng(num(r['standin_minus_server_exit_ms']) for r in rs)}")
for sc, b in [('d1', 'cx'), ('d1', 'cxnew'), ('d2', 'cx'), ('d2', 'cxnew'), ('d3', 'extra'), ('d3', 'cxnew')]:
    rs = sel(sc, b)
    lines.append(f"{b}/{sc}: server death minus client exit {rng(num(r['server_exit_vs_client_exit_ms']) for r in rs)}; server death after its last request (tools/call) {rng(num(r['server_exit_vs_last_recv_ms']) for r in rs)}; stand-in after server {rng(num(r['standin_minus_server_exit_ms']) for r in rs)}")
lines.append(f"e6: claude exit after EOF {rng(num(r['hosted_claude_exit_vs_eof_ms']) for r in sel('e6'))}")
for sc in ['a', 'b1', 'c1', 'e1', 'e2', 'e3', 'e4']:
    lines.append(f"{sc}: claude exit after EOF {rng(num(r['client_exit_vs_eof_ms']) for r in sel(sc))}")
with open(rf'{S}\ranges.txt', 'w', encoding='utf-8') as f:
    f.write('\n'.join(lines) + '\n')
print('\n'.join(lines))
