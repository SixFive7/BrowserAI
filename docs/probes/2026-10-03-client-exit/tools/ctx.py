# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import mmap, sys, re

# usage: ctx.py <file> <window> <needle> [<needle> ...]
# Prints every occurrence of each needle with its byte offset, 1-based line number
# (counted in the file), and +/- window bytes of context, non-printables escaped.
path = sys.argv[1]
win = int(sys.argv[2])
needles = sys.argv[3:]
with open(path, 'rb') as f:
    mm = mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ)
    size = mm.size()
    for nd in needles:
        b = nd.encode('utf-8')
        pos = 0
        hits = []
        while True:
            i = mm.find(b, pos)
            if i < 0:
                break
            hits.append(i)
            pos = i + 1
        print(f'=== needle {nd!r}: {len(hits)} hits')
        for i in hits:
            s = max(0, i - win)
            e = min(size, i + len(b) + win)
            chunk = mm[s:e]
            line = mm[:i].count(b'\n') + 1 if size < 50_000_000 else -1
            txt = chunk.decode('utf-8', errors='replace')
            txt = ''.join(c if (c.isprintable() or c in ' ') else '\\x%02x' % ord(c) if ord(c) < 256 else c for c in txt)
            print(f'--- offset {i} (0x{i:x}) line {line}')
            print(txt)
