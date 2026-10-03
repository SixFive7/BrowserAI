# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

"""Group analysis rows by (batch, scenario, delay) and print counts, outcomes and min/median/max of the key deltas."""
import csv, sys, statistics, re, collections

def num(x):
    try:
        return float(x)
    except (TypeError, ValueError):
        return None

def stats(vals):
    v = [x for x in vals if x is not None]
    if not v:
        return '-'
    return f"{min(v):.0f}/{statistics.median(v):.0f}/{max(v):.0f} (n={len(v)})"

def main():
    out_rows = []
    for path in sys.argv[1:-1]:
        batch = path.split('analysis-')[-1].replace('.tsv', '')
        with open(path, encoding='utf-8') as f:
            for row in csv.DictReader(f, delimiter='\t'):
                m = re.match(r'^(?P<sc>[a-z]\d?(?:-[a-z]+)?)-d(?P<d>\d+)-r(?P<r>\d+)$', row['run'])
                if not m:
                    continue
                row['batch'] = batch
                row['scenario'] = m.group('sc')
                row['delay'] = int(m.group('d'))
                out_rows.append(row)
    groups = collections.OrderedDict()
    for r in out_rows:
        groups.setdefault((r['batch'], r['scenario'], r['delay']), []).append(r)
    lines = ['batch\tscenario\tdelay_ms\truns\teof_seen\toutcomes\teof_to_server_exit_ms(min/med/max)\tstandin\tstandin_minus_server_ms(min/med/max)\tclient_exit_codes\tclient_exit_minus_server_exit_ms(min/med/max)\tlate_killers']
    for (b, sc, d), rs in sorted(groups.items(), key=lambda kv: (kv[0][0], kv[0][1], kv[0][2])):
        eof = collections.Counter(r['eof_seen'] for r in rs)
        outc = collections.Counter(r['server_outcome'] for r in rs)
        st = collections.Counter(r['standin_exit'].split(' (')[0] for r in rs)
        cc = collections.Counter(r['client_exit_code'] for r in rs)
        killers = collections.Counter()
        for r in rs:
            for k in (r.get('late_children') or '').split(' | '):
                if k.startswith('KILLER'):
                    killers[k.split(' ')[0]] += 1
        lines.append('\t'.join([
            b, sc, str(d), str(len(rs)),
            ','.join(f'{k}:{v}' for k, v in eof.items()),
            ','.join(f'{k}:{v}' for k, v in outc.items()),
            stats(num(r['eof_to_server_exit_ms(utc)']) for r in rs),
            ','.join(f'{k}:{v}' for k, v in st.items()),
            stats(num(r['standin_minus_server_exit_ms']) for r in rs),
            ','.join(f'{k}:{v}' for k, v in cc.items()),
            stats((-num(r['server_exit_vs_client_exit_ms']) if num(r['server_exit_vs_client_exit_ms']) is not None else None) for r in rs),
            ','.join(f'{k}:{v}' for k, v in killers.items()) or '-',
        ]))
    with open(sys.argv[-1], 'w', encoding='utf-8') as f:
        f.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))

if __name__ == '__main__':
    main()
