"""One row per Q369 Claude Code run, and a per-scenario rollup.

usage: python table.py <batch-dir> [<batch-dir> ...] > table.tsv
Columns: run, launches, tools/list per launch, list_changed sent (launch@time), refresh lines in the
client's debug log, first request that offered a real tool, real tool served (by which launch),
placeholder still offered after a real tool appeared, "No such tool" errors, mcp_status tool lists,
stream-json init tool lists after the first, client exit code.
"""
import json, os, re, sys

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

def row(run):
    name = os.path.basename(run)
    wire = jl(os.path.join(run, 'ph.wire.jsonl'))
    launches = sorted(set(w['launch'] for w in wire))
    lists = {L: 0 for L in launches}
    calls = {L: [] for L in launches}
    notif = []
    for w in wire:
        fr = w['frame'] if isinstance(w['frame'], dict) else {}
        if w['direction'] == 'client->server' and fr.get('method') == 'tools/list':
            lists[w['launch']] += 1
        if w['direction'] == 'client->server' and fr.get('method') == 'tools/call':
            calls[w['launch']].append(fr.get('params', {}).get('name'))
        if w['direction'] == 'server->client' and fr.get('method') == 'notifications/tools/list_changed':
            notif.append(f"L{w['launch']}@{w['at'][11:19]}")
    dbg = os.path.join(run, 'claude-debug.log')
    refresh = 0
    if os.path.exists(dbg):
        for line in open(dbg, encoding='utf-8', errors='replace'):
            if 'Received tools/list_changed notification, refreshing tools' in line:
                refresh += 1
    seen = jl(os.path.join(run, 'model.seen.jsonl'))
    first_real = None
    stale_after_real = False
    for s in seen:
        names = [t['name'] for t in s.get('browseraiTools', [])]
        if first_real is None and any(n.endswith('browserai_list') for n in names):
            first_real = s['n'] - seen[0]['n'] + 1
        if first_real is not None and any(n.endswith('update_in_flight') for n in names):
            stale_after_real = True
    served = []
    nosuch = 0
    out = os.path.join(run, 'client-stdout.log')
    inits = []
    statuses = []
    if os.path.exists(out):
        txt = open(out, encoding='utf-8', errors='replace').read()
        served = sorted(set(re.findall(r'REAL-TOOL-SERVED launch=(\d+)', txt)))
        nosuch = len(re.findall(r'No such tool available: mcp__browserai__update_in_flight', txt)) // 1
        for line in txt.splitlines():
            parts = line.split('\t', 5)
            if len(parts) < 6 or not parts[5].startswith('{'):
                continue
            try:
                e = json.loads(parts[5])
            except Exception:
                continue
            if e.get('type') == 'system' and e.get('subtype') == 'init':
                inits.append('+'.join(sorted(x.replace('mcp__browserai__', '') for x in e.get('tools', []) if 'browserai' in x)) or '-')
            if e.get('type') == 'control_response':
                inner = (e.get('response') or {}).get('response')
                if isinstance(inner, dict) and 'mcpServers' in inner:
                    for srv in inner['mcpServers']:
                        if srv.get('name') == 'browserai':
                            statuses.append(f"{srv.get('status')}:{'+'.join(t.get('name') for t in (srv.get('tools') or []))}")
    else:
        # TUI runs: the served line is in the server log
        lp = os.path.join(run, 'ph.launches.log')
        if os.path.exists(lp):
            served = sorted(set(re.findall(r'launch=(\d+) pid=\d+ tools/call #\d+ browserai_\w+ SERVED', open(lp, encoding='utf-8').read())))
    # The server's own log is the authority on which launch served a real call (a pseudoconsole's
    # output wraps lines, so the client's text alone can miss it).
    lp = os.path.join(run, 'ph.launches.log')
    if os.path.exists(lp):
        served = sorted(set(served) | set(re.findall(r'launch=(\d+) pid=\d+ tools/call #\d+ browserai_\w+ SERVED', open(lp, encoding='utf-8').read())))
    res = {}
    rp = os.path.join(run, 'result.json')
    if os.path.exists(rp):
        res = json.load(open(rp, encoding='utf-8'))
    return {
        'run': name, 'launches': len(launches),
        'lists': ','.join(f'L{L}:{lists[L]}' for L in launches),
        'calls': ' '.join(f"L{L}:{'+'.join(c or '-' for c in calls[L]) or '-'}" for L in launches),
        'list_changed_sent': ' '.join(notif) or '-', 'client_refresh_lines': refresh,
        'first_req_with_real_tool': first_real if first_real is not None else '-',
        'real_served_by': ','.join(f'L{x}' for x in served) or '-',
        'placeholder_still_offered_after_real': 'yes' if stale_after_real else ('-' if first_real is None else 'no'),
        'no_such_tool_errors': nosuch, 'mcp_status': ' | '.join(statuses) or '-', 'inits': ' | '.join(inits) or '-',
        'exit': res.get('clientExitCode'), 'waitTimeout': res.get('waitTimeout') or '-',
    }

def main():
    rows = []
    for b in sys.argv[1:]:
        for d in sorted(os.listdir(b)):
            p = os.path.join(b, d)
            if os.path.isdir(p) and os.path.exists(os.path.join(p, 'harness.log')):
                rows.append(row(p))
    if not rows:
        return
    cols = list(rows[0].keys())
    sys.stdout.reconfigure(encoding='utf-8')
    print('\t'.join(cols))
    for r in rows:
        print('\t'.join(str(r[c]) for c in cols))

if __name__ == '__main__':
    main()
