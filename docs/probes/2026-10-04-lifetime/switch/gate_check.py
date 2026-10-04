# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Exit 0 when a switch.mjs run worked end to end well enough that the rest of a
# batch is worth the lock: no fatal error, init answered, the browser came back
# after the resume, and the server exited. Exit 1 otherwise, with the reason.
import json
import sys

r = json.load(open(sys.argv[1], encoding='utf-8'))
problems = []
if r.get('fatal'):
    problems.append('fatal: ' + r['fatal'][:300])
steps = r.get('steps', [])
init = next((s for s in steps if s['label'] == 'init'), None)
if not init or init.get('isError'):
    problems.append('init failed: ' + (init or {}).get('text', 'missing')[:300])
if r.get('scenario') == 'switch':
    if not r.get('after'):
        problems.append('no tab was read after the resume')
    if not r.get('before'):
        problems.append('no tab was read before the close')
if r.get('serverExit') == 'timeout':
    problems.append('the server did not exit within 60 s of its stdin closing')
errors = [s for s in steps if s.get('isError')]
print(f"steps={len(steps)} errors={len(errors)} before={len(r.get('before', []))} after={len(r.get('after', []))}")
for s in errors[:8]:
    print('  error step:', s['label'], '|', s['text'][:200].replace('\n', ' '))
if problems:
    print('GATE FAILED:', '; '.join(problems))
    sys.exit(1)
print('GATE PASSED')
