"""Summarise Q369 Claude Code runs.

usage: python summarize.py <run-dir> [<run-dir> ...]   (or a batch dir: every subdir with result.json)
Prints, per run: the server launches and what each was sent (tools/list count, list_changed sent,
tools/call names), the API requests the scripted model received (which browserai tools were
offered, what it did), the client's own debug lines about list_changed / reconnects, the
stream-json init and mcp_status payloads, and the harness marks.
"""
import json, os, re, sys

def load_jsonl(p):
    out = []
    if not os.path.exists(p):
        return out
    with open(p, encoding='utf-8', errors='replace') as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            try:
                out.append(json.loads(line))
            except Exception:
                pass
    return out

def harness_lines(run):
    p = os.path.join(run, 'harness.log')
    if not os.path.exists(p):
        return []
    rows = []
    for line in open(p, encoding='utf-8', errors='replace'):
        parts = line.rstrip('\n').split('\t')
        if len(parts) >= 6:
            rows.append(parts)
    return rows

def stdout_events(run):
    p = os.path.join(run, 'client-stdout.log')
    evs = []
    if not os.path.exists(p):
        return evs
    for line in open(p, encoding='utf-8', errors='replace'):
        parts = line.rstrip('\n').split('\t', 5)
        if len(parts) < 6 or parts[4] != 'OUT':
            continue
        t, utc, payload = parts[0], parts[1], parts[5]
        if payload.startswith('{'):
            try:
                evs.append((float(t), utc, json.loads(payload)))
            except Exception:
                evs.append((float(t), utc, {'_unparsed': payload[:300]}))
    return evs

DEBUG_PAT = re.compile(r'MCP server "browserai"|list_changed|refreshing tools|\[MCP\]|mcp_reconnect|mcp_toggle|[Aa]uto.?reconnect|No such tool|tools/list|ToolSearch')

def summarize(run):
    name = os.path.basename(run.rstrip('\\/'))
    print('=' * 100)
    print(f'RUN {name}')
    res = {}
    rp = os.path.join(run, 'result.json')
    if os.path.exists(rp):
        res = json.load(open(rp, encoding='utf-8'))
    print(f"  client exit={res.get('clientExitCode')} harnessKilled={res.get('harnessKilledClient')} waitTimeout={res.get('waitTimeout')} start={res.get('startUtc')}")
    # marks
    for parts in harness_lines(run):
        if parts[4] in ('MARK', 'TOUCHED', 'SNAP', 'WAIT_TIMEOUT', 'ACTION_CLOSE_STDIN', 'CLIENT_EXITED', 'TYPED') :
            print(f"  [harness {parts[1][11:23]}] {parts[4]} {parts[5][:160]}")
    # server
    wire = load_jsonl(os.path.join(run, 'ph.wire.jsonl'))
    launches = sorted(set(w['launch'] for w in wire))
    print(f'  SERVER launches={launches}')
    for L in launches:
        ws = [w for w in wire if w['launch'] == L]
        seq = []
        for w in ws:
            fr = w['frame'] if isinstance(w['frame'], dict) else {}
            m = fr.get('method')
            if w['direction'] == 'client->server':
                if m == 'tools/call':
                    seq.append(f"{w['at'][11:23]} <-call {fr.get('params', {}).get('name')}")
                elif m:
                    seq.append(f"{w['at'][11:23]} <-{m}")
            else:
                if m:
                    seq.append(f"{w['at'][11:23]} ->{m}")
                elif 'result' in fr and isinstance(fr['result'], dict) and 'tools' in fr['result']:
                    seq.append(f"{w['at'][11:23]} ->tools[{','.join(t['name'] for t in fr['result']['tools'])}]")
        print(f'   launch {L} pid={ws[0]["pid"]}: ' + ' | '.join(seq))
    lp = os.path.join(run, 'ph.launches.log')
    if os.path.exists(lp):
        for line in open(lp, encoding='utf-8', errors='replace'):
            if any(k in line for k in ('UPDATE DONE', 'TERMINATING', 'SEND notifications', 'stdin EOF', 'process exit')):
                print('   ' + line.rstrip()[11:])
    # model
    seen = load_jsonl(os.path.join(run, 'model.seen.jsonl'))
    print(f'  MODEL requests={len(seen)}')
    for s in seen:
        names = [t['name'] for t in s.get('browseraiTools', [])]
        res_txt = ''
        if s.get('browseraiResults'):
            last = s['browseraiResults'][-1]
            res_txt = f" lastResult[{last['tool']}{' ERR' if last['is_error'] else ''}]={last['text'][:70]!r}"
        other = ''
        if s.get('otherResults'):
            o = s['otherResults'][-1]
            other = f" otherResult[{o['tool']}{' ERR' if o['is_error'] else ''}]={o['text'][:90]!r}"
        instr = ('P' if s.get('phInstrAnywhere') else '') + ('R' if s.get('realInstrAnywhere') else '')
        print(f"   #{s['n']} {s['at'][11:23]} model={s.get('model')} offered={names} instr={instr or '-'} msgs={s['messages']} -> {s['decision']}{res_txt}{other}")
    # stream-json
    evs = stdout_events(run)
    for t, utc, e in evs:
        typ = e.get('type')
        if typ == 'system' and e.get('subtype') == 'init':
            tools = [x for x in e.get('tools', []) if 'browserai' in x]
            print(f"  [init {utc[11:23]}] tools={tools} mcp_servers={e.get('mcp_servers')}")
        elif typ == 'control_response':
            r = e.get('response', {})
            inner = r.get('response') if isinstance(r, dict) else None
            if isinstance(inner, dict) and 'mcpServers' in inner:
                for srv in inner['mcpServers']:
                    if srv.get('name') == 'browserai':
                        print(f"  [mcp_status {utc[11:23]}] status={srv.get('status')} tools={[x.get('name') for x in srv.get('tools', []) or []]} serverInfo={srv.get('serverInfo')} error={srv.get('error')}")
            else:
                print(f"  [control_response {utc[11:23]}] {json.dumps(r)[:200]}")
        elif typ == 'result':
            print(f"  [result {utc[11:23]}] subtype={e.get('subtype')} is_error={e.get('is_error')} result={str(e.get('result'))[:80]!r}")
        elif typ == 'user':
            for b in (e.get('message', {}).get('content') or []):
                if isinstance(b, dict) and b.get('type') == 'tool_result':
                    c = b.get('content')
                    txt = c if isinstance(c, str) else ' '.join(x.get('text', '') for x in c if isinstance(x, dict)) if isinstance(c, list) else str(c)
                    print(f"  [tool_result {utc[11:23]}]{' ERR' if b.get('is_error') else ''} {txt[:110]!r}")
        elif typ == 'system' and e.get('subtype') not in ('init',):
            st = e.get('subtype')
            if st and ('mcp' in st or 'tool' in st):
                print(f"  [system {utc[11:23]}] {json.dumps(e)[:220]}")
    # debug log
    dp = os.path.join(run, 'claude-debug.log')
    if os.path.exists(dp):
        n = 0
        for line in open(dp, encoding='utf-8', errors='replace'):
            if DEBUG_PAT.search(line):
                n += 1
                if n <= 60:
                    print('   dbg ' + line.rstrip()[:230])
        print(f'  (debug lines matched: {n})')

def main():
    targets = []
    for a in sys.argv[1:]:
        if os.path.exists(os.path.join(a, 'result.json')) or os.path.exists(os.path.join(a, 'harness.log')):
            targets.append(a)
        else:
            for d in sorted(os.listdir(a)):
                p = os.path.join(a, d)
                if os.path.isdir(p) and (os.path.exists(os.path.join(p, 'harness.log'))):
                    targets.append(p)
    for t in targets:
        summarize(t)

if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main()
