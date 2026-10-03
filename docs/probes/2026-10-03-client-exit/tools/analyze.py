# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

"""Summarise every run directory under the given roots into one TSV row per dummy-server instance.

Times: the dummy logs QPC (ms) and precise UTC; the observer records the kernel's exit FILETIME and
the QPC at which its wait returned. Deltas against EOF use UTC (server's EOF line vs kernel exit
time), which are both the precise system clock; QPC deltas are given beside them as a cross-check.
"""
import json, os, sys, glob, datetime

def parse_utc(s):
    if not s:
        return None
    s = s.rstrip('Z')
    if '.' in s:
        head, frac = s.split('.')
        frac = (frac + '000000')[:6]
        s = head + '.' + frac
    return datetime.datetime.fromisoformat(s).replace(tzinfo=datetime.timezone.utc)

def ms(a, b):
    if a is None or b is None:
        return None
    return round((a - b).total_seconds() * 1000.0, 1)

def fmt(x):
    return '' if x is None else str(x)

def read_log(path):
    rows = []
    with open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            p = line.rstrip('\n').split('\t')
            if len(p) < 6:
                continue
            rows.append({'qpc': float(p[0]), 'utc': parse_utc(p[1]), 'pid': p[2], 'role': p[3], 'ev': p[4], 'detail': p[5]})
    return rows

def analyze_run(d):
    res_path = os.path.join(d, 'result.json')
    if not os.path.exists(res_path):
        return []
    res = json.load(open(res_path, encoding='utf-8'))
    procs = res.get('procs', [])
    byPid = {str(p['pid']): p for p in procs}
    client = next((p for p in procs if p['role'] == 'client'), None)
    harness = read_log(os.path.join(d, 'harness.log')) if os.path.exists(os.path.join(d, 'harness.log')) else []
    actions = [h for h in harness if h['ev'].startswith('ACTION_')]
    killers = [p for p in procs if p['role'].startswith('KILLER') or p['role'].startswith('other:')]
    out = []
    server_logs = sorted(glob.glob(os.path.join(d, 'server-*.log')))
    if not server_logs:
        out.append({'run': os.path.basename(d), 'note': 'NO SERVER LOG'})
    for sl in server_logs:
        rows = read_log(sl)
        spid = os.path.basename(sl)[len('server-'):-len('.log')]
        start = next((r for r in rows if r['ev'] == 'START'), None)
        eof = next((r for r in rows if r['ev'] == 'EOF'), None)
        exitline = next((r for r in rows if r['ev'] == 'EXIT'), None)
        closing = next((r for r in rows if r['ev'] == 'CLOSING_JOB'), None)
        alive = [r for r in rows if r['ev'] == 'ALIVE']
        job = next((r['detail'] for r in rows if r['ev'] == 'JOB'), '')
        ctrl = [r['detail'] for r in rows if r['ev'] == 'CTRL']
        pexit = next((r for r in rows if r['ev'] == 'PARENT_EXIT'), None)
        recv_list = [r['detail'].split(' ')[0].replace('method=', '') for r in rows if r['ev'] == 'RECV']
        recv = []
        for m in recv_list:
            if m not in recv:
                recv.append(m)
        recv = [f"{m}x{recv_list.count(m)}" if recv_list.count(m) > 1 else m for m in recv]
        delay = None
        if start:
            for tok in start['detail'].split(' '):
                if tok.startswith('delayMs='):
                    delay = int(tok.split('=')[1])
        sp = byPid.get(spid)
        s_exit_utc = parse_utc(sp['exitUtc']) if sp and sp.get('exitUtc') else None
        s_code = sp.get('exitCode') if sp else None
        # stand-ins whose parent is this server
        stand = [p for p in procs if p['role'] == 'standin' and str(p['ppid']) == spid]
        st = stand[0] if stand else None
        st_exit_utc = parse_utc(st['exitUtc']) if st and st.get('exitUtc') else None
        eof_utc = eof['utc'] if eof else None
        if s_code == 77:
            outcome = 'self-exit (77)'
        elif s_code == 1:
            outcome = 'KILLED code 1'
        elif s_code == 0:
            outcome = 'KILLED code 0 (job close)'
        elif s_code is None:
            outcome = 'not observed / still alive'
        else:
            outcome = f'code {s_code}'
        if st is None:
            st_how = 'no standin observed'
        elif st.get('exitCode') == 0:
            st_how = 'code 0 (job close)'
        elif st.get('exitCode') == 1:
            st_how = 'code 1 (TerminateProcess/taskkill/TerminateJobObject)'
        else:
            st_how = f"code {st.get('exitCode')}"
        # Late children: every process (other than consoles and the rig's own) created after the
        # server's last received message, i.e. anything started while the session was ending.
        last_recv = max((r['utc'] for r in rows if r['ev'] == 'RECV'), default=None)
        kdesc = []
        for k in killers:
            kc = parse_utc(k['createdUtc'])
            if last_recv is not None and kc is not None and kc < last_recv:
                continue
            ref = eof_utc if eof_utc else last_recv
            kdesc.append(f"{k['role']} pid={k['pid']} ppid={k['ppid']}{' (client)' if client and str(k['ppid']) == str(client['pid']) else ''} created_vs_{'eof' if eof_utc else 'lastrecv'}_ms={fmt(ms(kc, ref))} exit_vs_{'eof' if eof_utc else 'lastrecv'}_ms={fmt(ms(parse_utc(k.get('exitUtc')), ref))} code={k.get('exitCode')} cmd={k['cmd'][:160]}")
        c_exit = parse_utc(client['exitUtc']) if client and client.get('exitUtc') else None
        # When the root is a host (node standing in for the extension host), the client proper
        # is the claude.exe it spawned.
        mid = next((p for p in procs if client and str(p['ppid']) == str(client['pid'])
                    and p['image'].lower().endswith('claude.exe') and '--input-format' in p['cmd']), None)
        mid_exit = parse_utc(mid['exitUtc']) if mid and mid.get('exitUtc') else None
        act = []
        for a in actions:
            act.append(f"{a['ev'].replace('ACTION_', '')}@{fmt(ms(a['utc'], eof_utc))}ms_vs_eof")
        last_alive_vs_eof = ms(alive[-1]['utc'], eof_utc) if alive and eof_utc else None
        row = {
            'run': os.path.basename(d),
            'server_pid': spid,
            'delay_ms': delay,
            'recv': '+'.join(recv),
            'eof_seen': 'yes' if eof else 'NO',
            'server_outcome': outcome,
            'eof_to_server_exit_ms(utc)': ms(s_exit_utc, eof_utc),
            'eof_to_server_exit_ms(qpc_seen)': round(float(sp['exitQpcMs']) - eof['qpc'], 1) if (sp and sp.get('exitQpcMs') and eof) else None,
            'last_alive_after_eof_ms': last_alive_vs_eof,
            'closing_job_logged': 'yes' if closing else 'no',
            'standin_exit': st_how,
            'standin_minus_server_exit_ms': ms(st_exit_utc, s_exit_utc),
            'client_exit_code': client.get('exitCode') if client else None,
            'client_exit_vs_eof_ms': ms(c_exit, eof_utc),
            'server_exit_vs_client_exit_ms': ms(s_exit_utc, c_exit),
            'parent_exit_vs_eof_ms': ms(pexit['utc'], eof_utc) if (pexit and eof_utc) else None,
            'actions': ' '.join(act),
            'hosted_claude_exit_code': mid.get('exitCode') if mid else None,
            'hosted_claude_exit_vs_eof_ms': ms(mid_exit, eof_utc) if mid else None,
            'server_exit_vs_hosted_claude_exit_ms': ms(s_exit_utc, mid_exit) if mid else None,
            'server_exit_vs_last_recv_ms': ms(s_exit_utc, last_recv),
            'client_exit_vs_last_recv_ms': ms(c_exit, last_recv),
            'late_children': ' | '.join(kdesc),
            'ctrl_events': ','.join(ctrl),
            'server_job': job[:120],
            'server_exit_utc': sp.get('exitUtc') if sp else None,
            'eof_utc': eof_utc.isoformat() if eof_utc else None,
        }
        out.append(row)
    return out

def main():
    roots = sys.argv[1:-1]
    out_path = sys.argv[-1]
    rows = []
    for r in roots:
        for d in sorted(glob.glob(os.path.join(r, '*'))):
            if os.path.isdir(d):
                rows.extend(analyze_run(d))
    cols = []
    for row in rows:
        for k in row:
            if k not in cols:
                cols.append(k)
    with open(out_path, 'w', encoding='utf-8') as f:
        f.write('\t'.join(cols) + '\n')
        for row in rows:
            f.write('\t'.join(fmt(row.get(c)) for c in cols) + '\n')
    print(f'{len(rows)} rows -> {out_path}')

if __name__ == '__main__':
    main()
