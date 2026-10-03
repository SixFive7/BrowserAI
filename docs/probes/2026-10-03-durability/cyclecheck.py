# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
# Scratch rig (durability research, 2026-10-03; written by the replacement agent). The Chromium
# crash cycles: what the relaunch after the first kill restored, whether that session wrote a
# session file after a new tab was opened in it, and what the session files held after the second
# kill. Reads results/cycle.jsonl.
import json, os
root = r'C:\Source\SixFive7\BrowserAI\.work\durability\results'
for line in open(os.path.join(root, 'cycle.jsonl'), encoding='utf-8-sig'):
    o = json.loads(line)
    if o['browser'] != 'chromium':
        continue
    first = o.get('inspectAfterKill') or {}
    mid = o.get('inspectAfterMidKill') or {}
    urls = (o.get('mid') or {}).get('urlsAfterSettle', [])
    shown = [u.split('127.0.0.1:47911')[-1] for u in urls]
    writes = [e for e in o.get('midEvents', []) if 'Sessions' in e]
    print('%-22s exit_type after kill 1: %-8s patched: %-15s relaunch showed: %s' % (o['id'], first.get('exitType'), first.get('patchedExitType') or '-', shown))
    print('    session-file writes in the 6 s after the new tab: %s' % (writes or 'none'))
    print('    session files after kill 2: %s' % [(f['f'], f['bytes'], f['tokens']) for f in mid.get('sessionFiles', [])])
    print('    restored by the final relaunch: %s' % ((o.get('survived') or {}).get('tabsRestored') or 'nothing'))
