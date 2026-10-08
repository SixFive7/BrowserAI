# SPDX-FileCopyrightText: 2026 Jori Huisman
# SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

# Sends a BrowserAI server the two opening frames Claude Code sends since 2026-09-30, and prints what
# came back: server/discover carrying revision 2026-07-28 as per-request metadata, then, on the same
# connection, the initialize handshake at 2025-11-25 and tools/list.
# Usage: python opening.py <the server's executable> [its arguments]: BrowserAI.Server.exe with none when this
# ran on 2026-10-08, and BrowserAI.exe --mcp since the one executable of the same day.
# It starts the server with no window and ends it by closing its input; it starts no browser.
import json
import queue
import subprocess
import sys
import threading
import time

CREATE_NO_WINDOW = 0x08000000

server = subprocess.Popen(sys.argv[1:], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                          stderr=subprocess.DEVNULL, creationflags=CREATE_NO_WINDOW)
lines = queue.Queue()
threading.Thread(target=lambda: [lines.put(line) for line in iter(server.stdout.readline, b'')],
                 daemon=True).start()


def send(message):
    server.stdin.write((json.dumps(message) + '\n').encode())
    server.stdin.flush()


def answer(request_id, seconds=60):
    until = time.time() + seconds
    while time.time() < until:
        try:
            message = json.loads(lines.get(timeout=until - time.time()))
        except queue.Empty:
            break
        if message.get('id') == request_id:
            return message
    return None


capabilities = {'roots': {}}
send({'jsonrpc': '2.0', 'id': 1, 'method': 'server/discover', 'params': {'_meta': {
    'io.modelcontextprotocol/protocolVersion': '2026-07-28',
    'io.modelcontextprotocol/clientCapabilities': capabilities}}})
discover = answer(1)
print('server/discover:', json.dumps(discover)[:400])

send({'jsonrpc': '2.0', 'id': 2, 'method': 'initialize', 'params': {
    'protocolVersion': '2025-11-25', 'capabilities': capabilities,
    'clientInfo': {'name': 'opening-probe', 'version': '0'}}})
initialize = answer(2) or {}
print('initialize protocolVersion:', initialize.get('result', {}).get('protocolVersion'),
      'error:', initialize.get('error'))

send({'jsonrpc': '2.0', 'method': 'notifications/initialized'})
send({'jsonrpc': '2.0', 'id': 3, 'method': 'tools/list'})
listed = answer(3) or {}
print('tools/list:', len(listed.get('result', {}).get('tools', [])), 'tools; error:', listed.get('error'))

server.stdin.close()
server.wait(timeout=60)
print('exit', server.returncode)
