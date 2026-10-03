# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03; written by the replacement agent). The cost of the
# Firefox session-store lever under the form-and-scroll workload, per run and per lever: page timer
# lag, fill throughput, job CPU, job write-call bytes (which include IPC pipes), and the session
# file writes seen on disk with their sizes.
import json, os, collections, statistics as st
root = r'C:\Source\SixFive7\BrowserAI\.work\durability\results'
by = collections.defaultdict(list)
for line in open(os.path.join(root, 'supp.jsonl'), encoding='utf-8-sig'):
    o = json.loads(line)
    if o.get('kind') != 'work' or o.get('error'):
        continue
    io = o['io']; pg = o['page']
    rec = [e for e in o.get('events', []) if e.split('|')[1].endswith('recovery.jsonlz4')]
    sizes = [int(e.split('|')[2]) for e in rec if e.split('|')[2].isdigit()]
    r = dict(id=o['id'], lever=o['lever'], p50=pg['lagP50'], p95=pg['lagP95'], p99=pg['lagP99'], mx=pg['lagMax'],
             fills=pg.get('fills'), fillMs=pg.get('fillMs'), inputs=(pg.get('ops') or {}).get('input'), scrolls=(pg.get('ops') or {}).get('scroll'),
             cpu=(io['cpuUserMs'] + io['cpuKernelMs']) / 1000.0, wMB=io['writeBytes'] / 1e6, rMB=io['readBytes'] / 1e6,
             saves=len(rec), saveKB=sum(sizes) / 1000.0, idle=(o.get('idleMsAtStart'), o.get('idleMsAtEnd')))
    by[o['lever']].append(r)
    print('%-20s lag p50=%-4s p95=%-4s p99=%-4s max=%-5s fills=%-5s ms/fill=%-5.1f cpu=%6.1fs wMB=%6.1f rMB=%6.1f sessionWrites=%-3d (%.1f KB) idle=%s' % (
        r['id'], r['p50'], r['p95'], r['p99'], r['mx'], r['fills'], (r['fillMs'] or 0) / max(1, r['fills'] or 1), r['cpu'], r['wMB'], r['rMB'], r['saves'], r['saveKB'], r['idle']))
print()
for lv, rs in sorted(by.items()):
    def rng(k):
        v = sorted(x[k] for x in rs)
        return '%s-%s' % (round(v[0], 1), round(v[-1], 1))
    print('%-7s n=%d  lagP99 %s ms  lagMax %s ms  fills %s  cpu %s s  sessionWrites %s  writeMB %s' % (lv, len(rs), rng('p99'), rng('mx'), rng('fills'), rng('cpu'), rng('saves'), rng('wMB')))
