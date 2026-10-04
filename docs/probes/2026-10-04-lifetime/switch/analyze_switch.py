# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Reads every switch.mjs result under a batch directory and prints one line per
# run and one table per cell: what came back after browser_close and a resume
# with headed toggled, store by store, and which program each mode ran.
import glob
import json
import os
import sys


def tab_by_path(tabs, suffix):
    for t in tabs:
        url = (t.get('state') or {}).get('url') or ''
        if url.split('?')[0].endswith(suffix) or url.endswith(suffix):
            return t
    return None


def program(census, label):
    c = (census or {}).get(label) or {}
    p = c.get('program') or {}
    exe = p.get('mainExe') or '-'
    exe = exe.split('\\browsers\\')[-1] if '\\browsers\\' in exe else exe
    return f"{exe} {' '.join(f for f in p.get('flags', []) if f.startswith('--headless') or f == '-headless') or '(no headless switch)'} kinds={p.get('kinds')}"


def step_ms(steps, label):
    for s in steps:
        if s['label'] == label:
            return s.get('ms')
    return None


def main(root):
    rows = []
    for path in sorted(glob.glob(os.path.join(root, '*', 'rig', 'result.json'))):
        r = json.load(open(path, encoding='utf-8'))
        if r.get('scenario') != 'switch':
            continue
        run = r['run']
        prefix = run.split('-')[0]
        before, after = r.get('before', []), r.get('after', [])
        b_store, a_store = tab_by_path(before, '/store'), tab_by_path(after, '/store')
        b_form, a_form = tab_by_path(before, '/form'), tab_by_path(after, '/form')
        b_acct, a_acct = tab_by_path(before, '/account'), tab_by_path(after, '/account')
        st = (a_store or {}).get('state') or {}
        fs = (a_form or {}).get('state') or {}
        who = st.get('who') if isinstance(st.get('who'), dict) else {}
        expected_ls = f"{prefix}" if False else None
        res = {
            'id': os.path.basename(os.path.dirname(os.path.dirname(path))),
            'browser': r['browser'], 'from': r['from'],
            'tabs_before': [t['listed']['url'].split('/', 3)[-1] for t in before],
            'tabs_after': [t['listed']['url'].split('/', 3)[-1] for t in after],
            'current_after': [t['listed']['url'].split('/', 3)[-1] for t in after if t['listed'].get('current')],
            'signed_in_before': ((((b_store or {}).get('state') or {}).get('who')) or {}).get('user') if b_store else None,
            'signed_in_after': who.get('user'),
            'session_cookie_after': who.get('sessionCookie'),
            'js_persistent_after': who.get('jsPersistent') == run,
            'js_session_after': who.get('jsSession') == run,
            'ls_after': st.get('ls') == run,
            'idb_after': st.get('idb') == run,
            'ss_tab0_after': st.get('ss') == f'{run}-tab0',
            'ss_tab1_after': fs.get('ss') == f'{run}-tab1',
            'typed_after': fs.get('notes') == f'typed {run}',
            'history_before': ((b_store or {}).get('state') or {}).get('historyLength'),
            'history_after': st.get('historyLength'),
            'back_to': ((r.get('backTo') or {}).get('url') or '').split('/', 3)[-1] if isinstance(r.get('backTo'), dict) else None,
            'ua_before': ((b_store or {}).get('state') or {}).get('ua'),
            'ua_after': st.get('ua'),
            'brands_after': st.get('brands'),
            'visibility_after': st.get('visibility'),
            'nav_after': [((x.get('state') or {}).get('navType')) for x in after],
            'nav_before': [((x.get('state') or {}).get('navType')) for x in before],
            'program_before': program(r.get('census'), 'before'),
            'program_after': program(r.get('census'), 'after'),
            'close_ms': step_ms(r['steps'], 'browser_close'),
            'resume_ms': step_ms(r['steps'], 'resume with headed toggled'),
            'first_call_ms': step_ms(r['steps'], 'after: first browser call (list tabs)'),
            'first_list_count': len((r.get('restore') or {}).get('first') or []),
            'restore_readings': (r.get('restore') or {}).get('readings'),
            'restore_settle_ms': (r.get('restore') or {}).get('settleMs'),
            'resume_text': next((s['text'][:600] for s in r['steps'] if s['label'] == 'resume with headed toggled'), None),
            'errors': [f"{s['label']}: {s['text'][:160]}" for s in r['steps'] if s.get('isError')],
            'fatal': r.get('fatal'),
            'server_exit': r.get('serverExit'),
        }
        rows.append(res)
    json.dump(rows, open(os.path.join(root, 'switch-summary.json'), 'w', encoding='utf-8'), indent=1)
    for res in rows:
        print(json.dumps(res, ensure_ascii=False))


if __name__ == '__main__':
    main(sys.argv[1])
