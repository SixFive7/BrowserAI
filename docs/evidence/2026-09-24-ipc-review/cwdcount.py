import os, re, glob
base = os.path.join(os.environ['LOCALAPPDATA'], 'BrowserAI', 'logs')
logs = sorted(glob.glob(os.path.join(base, '*.log')))
pat = re.compile(r'^(\S+)\s.*pid=(\d+)@(\d+)\s+BrowserAI\.Startup\[1\]\s+BrowserAI (\S+) started\. pid=\d+ image=(.*?) cwd=(.*?) sqlite=')
installed = os.sep + 'browserai.app' + os.sep + 'current' + os.sep + 'browserai.server.exe'
starts = {}
for f in logs:
    with open(f, encoding='utf-8', errors='replace') as h:
        for line in h:
            if 'Startup[1]' not in line:
                continue
            m = pat.match(line)
            if not m:
                continue
            t, pid, created, ver, image, cwd = m.groups()
            if installed not in image.lower():
                continue
            starts[(pid, created)] = (t, ver, cwd)
def isroot(d):
    return os.path.exists(os.path.join(d, '.git'))
def inside(d):
    d = os.path.abspath(d)
    while True:
        if os.path.exists(os.path.join(d, '.git')):
            return True
        p = os.path.dirname(d)
        if p == d:
            return False
        d = p
times = sorted(v[0] for v in starts.values())
n = len(starts)
root = sum(1 for v in starts.values() if isroot(v[2]))
ins = sum(1 for v in starts.values() if inside(v[2]))
sys32 = sum(1 for v in starts.values() if v[2].lower().rstrip(os.sep).endswith(os.sep + 'windows' + os.sep + 'system32'))
gone = sum(1 for v in starts.values() if not os.path.isdir(v[2]))
print('logs', len(logs), 'installed-server starts', n, 'first', times[0] if times else None, 'last', times[-1] if times else None)
print('cwd is a repo root', root, 'cwd inside a repo', ins, 'system32', sys32, 'cwd missing now', gone)
vers = {}
for v in starts.values():
    vers[v[1]] = vers.get(v[1], 0) + 1
print('versions', vers)
