# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import sys
# usage: slice.py <file> <offset> <before> <after>
# Prints bytes [offset-before, offset+after) of the file, decoded as UTF-8 with replacement.
path, off, before, after = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
with open(path, 'rb') as f:
    s = max(0, off - before)
    f.seek(s)
    data = f.read(before + after)
txt = data.decode('utf-8', errors='replace')
sys.stdout.reconfigure(encoding='utf-8')
print(f'[slice of {path} from {s} to {s + len(data)}]')
print(txt)
