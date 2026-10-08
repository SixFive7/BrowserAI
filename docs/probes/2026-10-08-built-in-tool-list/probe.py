# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Asks the payload's own @playwright/mcp child for tools/list under five session-shaped configurations and
# compares the RAW bytes of the result member with the snapshot's tools, minified. No browser starts:
# tools/list is answered before any page exists. Everything it writes goes under <repository>\.work, which git ignores.
# Usage: python probe.py <repository root> [--snapshot <an older tools-list.json, for the positive control>]
import json
import os
import subprocess
import sys
import threading
import queue
import time

wt = sys.argv[1]
snapshot_override = sys.argv[3] if len(sys.argv) > 3 and sys.argv[2] == '--snapshot' else None
here = os.path.dirname(os.path.abspath(__file__))
node = os.path.join(wt, 'payload', 'node', 'node.exe')
cli = os.path.join(wt, 'payload', 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js')
snapshot_path = snapshot_override or os.path.join(wt, 'upstream-snapshots', 'tools-list.json')


def minify(raw: bytes) -> bytes:
    """Drops every whitespace byte outside a string; strings are copied byte for byte."""
    out = bytearray()
    in_string = False
    escaped = False
    for b in raw:
        if in_string:
            out.append(b)
            if escaped:
                escaped = False
            elif b == 0x5C:
                escaped = True
            elif b == 0x22:
                in_string = False
        elif b == 0x22:
            in_string = True
            out.append(b)
        elif b in (0x20, 0x09, 0x0A, 0x0D):
            continue
        else:
            out.append(b)
    return bytes(out)


def snapshot_tools_bytes() -> bytes:
    raw = open(snapshot_path, 'rb').read()
    compact = minify(raw)
    key = b'"tools":'
    i = compact.rindex(key)  # the last member of the document
    j = i + len(key)
    # The array runs to the document's final closing brace.
    assert compact.endswith(b'}'), compact[-20:]
    return compact[j:-1]


def raw_result(line: bytes) -> bytes:
    """The bytes of the result member exactly as the child wrote them."""
    key = b'"result":'
    i = line.index(key) + len(key)
    depth = 0
    in_string = False
    escaped = False
    for k in range(i, len(line)):
        b = line[k]
        if in_string:
            if escaped:
                escaped = False
            elif b == 0x5C:
                escaped = True
            elif b == 0x22:
                in_string = False
            continue
        if b == 0x22:
            in_string = True
        elif b in (0x7B, 0x5B):
            depth += 1
        elif b in (0x7D, 0x5D):
            depth -= 1
            if depth == 0:
                return line[i:k + 1]
    raise ValueError('unterminated result')


def ask(label: str, config: dict) -> bytes:
    d = os.path.join(wt, '.work', 'probe-built-in-tool-list', label)
    os.makedirs(d, exist_ok=True)
    for sub in ('profile', 'output', 'downloads', 'tmp', 'browsers'):
        os.makedirs(os.path.join(d, sub), exist_ok=True)
    config = json.loads(json.dumps(config).replace('@D@', d.replace('\\', '\\\\')))
    cfg = os.path.join(d, 'config.json')
    with open(cfg, 'w', encoding='utf-8') as f:
        json.dump(config, f, indent=2)
    env = dict(os.environ)
    env['PLAYWRIGHT_BROWSERS_PATH'] = os.path.join(d, 'browsers')
    env['TEMP'] = env['TMP'] = os.path.join(d, 'tmp')
    p = subprocess.Popen([node, cli, '--config', cfg, '--sandbox'], cwd=os.path.join(d, 'output'),
                         stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                         env=env, creationflags=0x08000000)
    q = queue.Queue()
    threading.Thread(target=lambda: [q.put(l) for l in iter(p.stdout.readline, b'')], daemon=True).start()

    def send(o):
        p.stdin.write((json.dumps(o) + '\n').encode())
        p.stdin.flush()

    def recv(i, t=60):
        end = time.time() + t
        while time.time() < end:
            try:
                line = q.get(timeout=end - time.time())
            except queue.Empty:
                break
            m = json.loads(line)
            if m.get('id') == i:
                return line.rstrip(b'\r\n')
        raise TimeoutError(label)

    send({'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {
        'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'BrowserAI', 'version': '0'}}})
    recv(1)
    send({'jsonrpc': '2.0', 'method': 'notifications/initialized'})
    send({'jsonrpc': '2.0', 'id': 2, 'method': 'tools/list'})
    line = recv(2)
    p.stdin.close()
    try:
        p.wait(timeout=30)
    except subprocess.TimeoutExpired:
        p.kill()
    open(os.path.join(d, 'tools-list.line'), 'wb').write(line)
    return raw_result(line)


caps7 = ['config', 'vision', 'devtools', 'storage', 'network', 'pdf', 'testing']
common_tail = {
    'capabilities': caps7, 'outputDir': '@D@\\output', 'saveSession': False, 'allowUnrestrictedFileAccess': False,
    'console': {'level': 'debug'}, 'snapshot': {'boxes': False}, 'codegen': 'none', 'filePaths': 'absolute',
    'timeouts': {'idle': 3600000}, 'webmcp': True,
}
chromium = {
    'browser': {
        'browserName': 'chromium', 'userDataDir': '@D@\\profile',
        'launchOptions': {'channel': 'chrome-for-testing', 'args': ['--hide-crash-restore-bubble'],
                          'ignoreDefaultArgs': ['about:blank'], 'headless': True, 'downloadsPath': '@D@\\downloads'},
        'contextOptions': {'viewport': {'width': 1920, 'height': 1080}, 'locale': 'en-US', 'ignoreHTTPSErrors': False,
                           'permissions': ['clipboard-read']},
    }, **common_tail}
firefox = {
    'browser': {
        'browserName': 'firefox', 'userDataDir': '@D@\\profile',
        'launchOptions': {'firefoxUserPrefs': {'toolkit.winRegisterApplicationRestart': False,
                                               'signon.rememberSignons': False},
                          'ignoreDefaultArgs': ['about:blank'], 'headless': True, 'downloadsPath': '@D@\\downloads'},
        'contextOptions': {'viewport': {'width': 1920, 'height': 1080}, 'locale': 'en-US', 'ignoreHTTPSErrors': False},
    }, **common_tail}
headed = json.loads(json.dumps(chromium))
headed['browser']['launchOptions']['headless'] = False
headed['timeouts'] = {'idle': 0}
transcript_har = json.loads(json.dumps(chromium))
transcript_har['saveSession'] = True
transcript_har['browser']['contextOptions']['serviceWorkers'] = 'block'
transcript_har['browser']['contextOptions']['recordHar'] = {'path': '@D@\\output\\network-x.har', 'mode': 'full',
                                                           'content': 'embed'}
transcript_har['browser']['contextOptions']['timezoneId'] = 'Europe/Berlin'
transcript_har['browser']['contextOptions']['ignoreHTTPSErrors'] = True

variants = {
    'snapshot-config': {'capabilities': json.load(open(snapshot_path, encoding='utf-8'))['declaredCapabilities']},
    'chromium-headless': chromium,
    'firefox-headless': firefox,
    'chromium-headed': headed,
    'chromium-transcript-har': transcript_har,
}

expected = b'{"tools":' + snapshot_tools_bytes() + b'}'
print('expected bytes:', len(expected))
for label, config in variants.items():
    got = ask(label, config)
    same = got == expected
    first = None
    if not same:
        for k in range(min(len(got), len(expected))):
            if got[k] != expected[k]:
                first = k
                break
    print(f'{label}: {len(got)} bytes, byte-identical={same}', '' if same else f'first difference at {first}: got {got[max(0, (first or 0) - 60):(first or 0) + 60]!r}')
