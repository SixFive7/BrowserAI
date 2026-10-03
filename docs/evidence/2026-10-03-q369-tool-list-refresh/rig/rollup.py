"""Roll the per-run tables up to one line per scenario and client.

usage: python rollup.py > runs/summary-by-arm.tsv
Claude Code rows come from table.py's columns; Codex rows from the run directories directly.
"""
import json, os, re, subprocess, sys, glob
from collections import defaultdict

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'runs')
CC_BATCHES = ['ccP', 'ccS', 'ccS2', 'ccKp', 'ccKp2', 'ccKs', 'tui', 'tui2', 'tui3', 'tuiK']
CX_BATCHES = ['cx', 'cx2', 'cxK']
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import table as T  # noqa: E402

def jl(p):
    out = []
    if os.path.exists(p):
        for line in open(p, encoding='utf-8', errors='replace'):
            line = line.strip()
            if line:
                try:
                    out.append(json.loads(line))
                except Exception:
                    pass
    return out

groups = defaultdict(list)
for b in CC_BATCHES:
    d = os.path.join(BASE, b)
    if not os.path.isdir(d):
        continue
    for r in sorted(os.listdir(d)):
        p = os.path.join(d, r)
        if not (os.path.isdir(p) and os.path.exists(os.path.join(p, 'harness.log'))):
            continue
        if b == 'ccKp' and r.startswith('PBr'):
            continue  # void: the scripted model never made the second call
        row = T.row(p)
        m = re.match(r'(.+)-(cli|vsc287)-r\d+$', r)
        groups[(b, m.group(1), m.group(2))].append(row)

print('\t'.join(['batch', 'scenario', 'client', 'runs', 'launches', 'tools/list per launch', 'list_changed honoured (client refresh line)', 'real tool offered to the model', 'real tool served by', 'placeholder still offered after real']))
for (b, sc, cl), rows in sorted(groups.items()):
    n = len(rows)
    def dist(key):
        c = defaultdict(int)
        for r in rows:
            c[str(r[key])] += 1
        return ' / '.join(f'{k} x{v}' for k, v in sorted(c.items()))
    real = sum(1 for r in rows if r['first_req_with_real_tool'] != '-')
    refresh = sum(1 for r in rows if r['client_refresh_lines'] and int(r['client_refresh_lines']) > 0)
    print('\t'.join([b, sc, cl, str(n), dist('launches'), dist('lists'), f'{refresh}/{n}', f'{real}/{n}', dist('real_served_by'), dist('placeholder_still_offered_after_real')]))

print()
print('\t'.join(['batch', 'scenario', 'codex', 'runs', 'thread launches', 'probe launches', 'list_changed sent', 'turn outcomes (tool:status per turn, in order)']))
cg = defaultdict(list)
for b in CX_BATCHES:
    d = os.path.join(BASE, b)
    if not os.path.isdir(d):
        continue
    for r in sorted(os.listdir(d)):
        p = os.path.join(d, r)
        if not os.path.isdir(p):
            continue
        m = re.match(r'(.+)-cx(155|160)-r\d+$', r)
        if not m:
            continue
        wire = jl(os.path.join(p, 'ph.wire.jsonl'))
        roles = {}
        notif = 0
        for w in wire:
            fr = w['frame'] if isinstance(w['frame'], dict) else {}
            if w['direction'] == 'client->server' and fr.get('method') == 'initialize':
                roles[w['launch']] = 'thread' if 'experimental' in fr.get('params', {}).get('capabilities', {}) else 'probe'
            if w['direction'] == 'server->client' and fr.get('method') == 'notifications/tools/list_changed':
                notif += 1
        outcomes = []
        drv = os.path.join(p, 'driver.log')
        if os.path.exists(drv):
            for line in open(drv, encoding='utf-8', errors='replace'):
                mm = re.search(r'NOTE#\d+ item/completed (\{.*)', line)
                if mm and '"type":"mcpToolCall"' in mm.group(1):
                    t = re.search(r'"tool":"(\w+)","status":"(\w+)"', mm.group(1))
                    if t:
                        outcomes.append(f'{t.group(1)}:{t.group(2)}')
        for ex in ('exec1.jsonl', 'exec2.jsonl'):
            for e in jl(os.path.join(p, ex)):
                it = e.get('item') or {}
                if e.get('type') == 'item.completed' and it.get('type') == 'mcp_tool_call':
                    outcomes.append(f"{ex[:5]}:{it.get('tool')}:{it.get('status')}")
        cg[(b, m.group(1), m.group(2))].append((sum(1 for v in roles.values() if v == 'thread'), sum(1 for v in roles.values() if v == 'probe'), notif, ' '.join(outcomes)))
for (b, sc, v), rows in sorted(cg.items()):
    n = len(rows)
    def d2(i):
        c = defaultdict(int)
        for r in rows:
            c[str(r[i])] += 1
        return ' / '.join(f'{k} x{c[k]}' for k in sorted(c))
    print('\t'.join([b, sc, v, str(n), d2(0), d2(1), d2(2), d2(3)]))
