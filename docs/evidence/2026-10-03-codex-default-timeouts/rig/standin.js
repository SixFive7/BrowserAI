// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A stand-in stdio MCP server for reading Codex's default startup and tool
// timeouts: it answers `initialize` after SI_INIT_DELAY_MS and a `tools/call`
// after SI_CALL_DELAY_MS, offers one tool, and logs every frame with its time
// since launch to SI_LOG. It never writes anything else to stdout.
'use strict';
const fs = require('fs');

const INIT_DELAY = Number(process.env.SI_INIT_DELAY_MS || 0);
const CALL_DELAY = Number(process.env.SI_CALL_DELAY_MS || 0);
const LOG = process.env.SI_LOG || 'standin.log';
const t0 = Date.now();
const log = (s) => fs.appendFileSync(LOG, `${new Date().toISOString()} +${Date.now() - t0}ms pid=${process.pid} ${s}\n`);
const send = (o) => process.stdout.write(JSON.stringify(o) + '\n');

log(`LAUNCH initDelayMs=${INIT_DELAY} callDelayMs=${CALL_DELAY}`);

function handle(m) {
  log(`RECV ${m.method ?? 'a response'} id=${m.id ?? '-'}`);
  if (m.method === 'initialize') {
    setTimeout(() => {
      send({
        jsonrpc: '2.0',
        id: m.id,
        result: {
          protocolVersion: (m.params && m.params.protocolVersion) || '2025-06-18',
          capabilities: { tools: {} },
          serverInfo: { name: 'timeout-standin', version: '1.0.0' },
        },
      });
      log('SENT the initialize result');
    }, INIT_DELAY);
  } else if (m.method === 'tools/list') {
    send({
      jsonrpc: '2.0',
      id: m.id,
      result: {
        tools: [{
          name: 'browserai_list',
          description: 'A stand-in tool that answers after a set delay.',
          inputSchema: { type: 'object', properties: { directory: { type: 'string' } } },
        }],
      },
    });
    log('SENT the tools/list result');
  } else if (m.method === 'tools/call') {
    setTimeout(() => {
      send({ jsonrpc: '2.0', id: m.id, result: { content: [{ type: 'text', text: `answered after ${CALL_DELAY} ms` }] } });
      log('SENT the tools/call result');
    }, CALL_DELAY);
  } else if (m.id !== undefined && m.method) {
    send({ jsonrpc: '2.0', id: m.id, result: {} });
    log(`SENT an empty result to ${m.method}`);
  }
}

let buffer = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
  buffer += chunk;
  let at;
  while ((at = buffer.indexOf('\n')) >= 0) {
    const line = buffer.slice(0, at).trim();
    buffer = buffer.slice(at + 1);
    if (line) {
      try { handle(JSON.parse(line)); } catch (e) { log(`UNPARSEABLE ${line.slice(0, 200)}`); }
    }
  }
});
process.stdin.on('end', () => { log('STDIN EOF'); process.exit(0); });
