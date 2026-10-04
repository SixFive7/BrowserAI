# Prints, for every window-close run of a batch, the server's version, the exit
# line, the refusal, the resume's note, catch_up's close line and what each tab
# held after the resume.
#   python read_windowclose.py <runs root> <batch>
import glob
import json
import os
import sys

root, batch = sys.argv[1], sys.argv[2]
for p in sorted(glob.glob(os.path.join(root, batch, 'windowclose-*', 'rig', 'result.json'))):
    r = json.load(open(p, encoding='utf-8'))
    run = os.path.basename(os.path.dirname(os.path.dirname(p)))
    v = (((r.get('handshake') or {}).get('init') or {}).get('result') or {}).get('serverInfo', {}).get('version')
    print('=====', run, v, 'fatal=', r.get('fatal'))
    print('  exit:', r.get('exit-windowclose'))
    for s in r['steps']:
        if s['label'] in ('the next call after the browser ended', 'resume after the browser ended'):
            print('  STEP', s['label'], 'err=', s['isError'])
            print('     ', s['text'][:900].replace('\n', ' / '))
        if s['label'] == 'catch_up after the resume':
            t = s['text']
            i = t.find('last recorded close')
            print('  CATCHUP', t[i:i + 200].replace('\n', ' / '))
    print('  after:', [(x['url'][-22:], {k: (x['state'] or {}).get(k) for k in ('ls', 'ss', 'notes', 'cookie')},
                        ((x['state'] or {}).get('who') or {}).get('sessionCookie')) for x in r.get('stateAfter', [])])
