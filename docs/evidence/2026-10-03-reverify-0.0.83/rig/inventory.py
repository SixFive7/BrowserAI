import os
import sys

# Counts, per file, the C0 control bytes the tree refuses and the occurrences of
# this machine's user-profile path, read from the environment so that the name
# itself is written nowhere in this script.
root = sys.argv[1] if len(sys.argv) > 1 else 'out'
user = os.path.basename(os.environ['USERPROFILE']).encode()
needles = [b'Users' + bytes([92]) + user, b'Users' + bytes([92, 92]) + user, b'Users/' + user]
tot = 0
n = 0
for dp, dns, fns in os.walk(root):
    rel = os.path.relpath(dp, root).replace(os.sep, '/')
    parts = rel.split('/')
    if any(p.startswith(('profile', 'conc-', 'session-', 'eftype')) for p in parts):
        continue
    if 'output' in parts or 'downloads' in parts:
        continue
    for f in fns:
        p = os.path.join(dp, f)
        s = os.path.getsize(p)
        b = open(p, 'rb').read()
        ctrl = sum(1 for c in b if c < 0x20 and c not in (9, 10, 13))
        prof = sum(b.count(x) for x in needles)
        tot += s
        n += 1
        print(f"{s:8d}  ctrl={ctrl:4d} prof={prof:3d}  {p}")
print(n, 'files', tot, 'bytes')
