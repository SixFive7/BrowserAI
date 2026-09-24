# Read-only: how long an installed BrowserAI server took from its first log record
# (Startup[1]) to serving stdio (Startup[2]), over every start in the process log.
# Pass an ISO time to count only starts before it.
import os, re, glob, statistics, sys
until = sys.argv[1] if len(sys.argv) > 1 else None
base = os.path.join(os.environ['LOCALAPPDATA'], 'BrowserAI', 'logs')
logs = sorted(glob.glob(os.path.join(base, '*.log')))
ts = re.compile(r'^(\S+)\s.*pid=(\d+)@(\d+)\s+BrowserAI\.Startup\[(1|2)\]\s+(.*)$')
from datetime import datetime
def parse(t):
    t = t.rstrip('Z')
    if '.' in t:
        head, frac = t.split('.')
        frac = (frac + '000000')[:6]
        t = head + '.' + frac
    return datetime.fromisoformat(t)
started = {}
serving = {}
kind = {}
for f in logs:
    with open(f, encoding='utf-8', errors='replace') as h:
        for line in h:
            if 'Startup[' not in line:
                continue
            m = ts.match(line)
            if not m:
                continue
            t, pid, created, ev, rest = m.groups()
            key = (pid, created)
            if until and t >= until:
                continue
            if ev == '1':
                started[key] = parse(t)
                kind[key] = 'installed' if 'browserai.app' in rest.lower() else 'other'
            else:
                serving.setdefault(key, parse(t))
inst = sorted(started[x] for x in started if x in serving and kind[x] == 'installed')
print('installed starts from', inst[0].isoformat() if inst else None, 'to', inst[-1].isoformat() if inst else None)
for k in ('installed', 'other'):
    d = [(serving[x] - started[x]).total_seconds() for x in started if x in serving and kind[x] == k]
    d.sort()
    if d:
        print(k, 'n', len(d), 'p50', round(statistics.median(d), 3), 'p10', round(d[len(d)//10], 3), 'p90', round(d[(len(d)*9)//10], 3), 'min', round(d[0], 3))
