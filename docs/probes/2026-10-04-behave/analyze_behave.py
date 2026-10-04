# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Reads every behave.mjs result under the batch directories named and prints one
# block per scenario: the user agent each request carried, the screenshot sizes
# and outcomes, and how each browser ended.
#
#   python analyze_behave.py <runs root> <batch>[,<batch>...]
import glob
import json
import os
import sys


def short(ua):
    if not ua:
        return ua
    return ua.replace('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) ', '').replace(' Safari/537.36', '')


def main(root, batches):
    for batch in batches:
        for path in sorted(glob.glob(os.path.join(root, batch, '*', 'rig', 'result.json'))):
            run = os.path.basename(os.path.dirname(os.path.dirname(path)))
            r = json.load(open(path, encoding='utf-8'))
            sc = r.get('scenario')
            head = f"{batch}/{run} [{sc} {r.get('browser')} headed={r.get('headed')}] serverExit={r.get('serverExit')} fatal={r.get('fatal')}"
            print(head)
            if sc in ('ua', 'restore'):
                p = r.get('page') or {}
                print('   page ua:', short(p.get('ua')), '| brands:', p.get('brands'), '| hev:', json.dumps(p.get('highEntropy'))[:300] if p.get('highEntropy') else None)
                seen = {}
                for h in r.get('siteHits') or []:
                    key = (h.get('dest'), short(h.get('ua')), h.get('chUa'), h.get('chUaFullVersionList'), h.get('chUaPlatformVersion'))
                    seen.setdefault(key, []).append(h.get('path'))
                for k, v in seen.items():
                    print('   hit', k, 'x', len(v), v[:4])
                for h in r.get('restoreHits') or []:
                    print('   restoreHit', h.get('path'), h.get('dest'), short(h.get('ua')), h.get('chUa'))
            if sc == 'tall':
                for i in r.get('images') or []:
                    print('   image', i.get('label'), 'h=', i.get('height'), 'isError=', i.get('isError'), 'inline=', i.get('images'), 'size=', i.get('size') or i.get('width'), '|', (i.get('text') or '')[:160].replace('\n', ' / '))
                an = os.path.join(os.path.dirname(path), 'analysis.json')
                if os.path.exists(an):
                    a = json.load(open(an, encoding='utf-8'))
                    print('   analysis:', json.dumps(a)[:1500])
            for k, v in r.items():
                if k.startswith('exit-'):
                    print('  ', k, v)
            if 'tabsAfter' in r:
                print('   tabsAfter:', [(t.get('url'), t.get('current')) for t in r['tabsAfter']])
            errs = [f"{s['label']}: {(s.get('text') or '')[:200]}" for s in r.get('steps', []) if s.get('isError')]
            for e in errs:
                print('   ERR', e.replace('\n', ' / '))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2].split(','))
