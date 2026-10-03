# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Scratch (Q302): print the Firefox call log out of a suite log's failure JSON.
import re, sys, json

for f in sys.argv[1:]:
    t = open(f, encoding='utf-8', errors='replace').read()
    m = re.search(r'Call log:(.*?)"', t, re.S)
    if not m:
        print('=====', f, 'no call log')
        continue
    s = m.group(1)
    s = s.replace('\\n', '\n').replace('\\u001b[2m', '').replace('\\u001b[22m', '').replace('\\\\', '\\')
    print('=====', f)
    print(s[:4000])
    m2 = re.search(r'\{"launcherPid".*?\}(?=\s)', t)
    print(m2.group(0)[:2000] if m2 else '(no launcher record)')
