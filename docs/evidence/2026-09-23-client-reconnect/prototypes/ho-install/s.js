// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// S: the real server, spawned by the relay. Lives in the install folder and is
// replaced by the apply. An ordinary MCP server with no handover logic at all.
const fs = require('fs');
const path = require('path');
const LOGDIR = process.env.HO_LOGDIR;
const TAG = process.env.HO_TAG || 'relay';
const ANNOUNCE = process.env.HO_ANNOUNCE_LIST_CHANGED === '1';
let VERSION = 'unknown';
try { VERSION = fs.readFileSync(path.join(__dirname, 'VERSION'), 'utf8').trim(); } catch (e) {}
const LF = path.join(LOGDIR, TAG + '.handover.log');
const log = (k, d) => fs.appendFileSync(LF, new Date().toISOString() + ' ' + process.pid + ' S(v' + VERSION + ') ' + k + ' ' + d + '\n');
log('LAUNCH', 'real server version=' + VERSION);
process.on('exit', (c) => { try { log('EXIT', 'code=' + c); } catch (e) {} });
const out = (o) => fs.writeSync(1, JSON.stringify(o) + '\n');
let calls = 0;
let buf = '';
if (ANNOUNCE && VERSION !== '1.0.0') {
  out({ jsonrpc: '2.0', method: 'notifications/tools/list_changed', params: {} });
  log('OUT', 'tools/list_changed announced by the post-apply server');
}
process.stdin.on('data', (d) => {
  buf += d.toString('utf8');
  let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim(); buf = buf.slice(nl + 1);
    if (!line) continue;
    let m; try { m = JSON.parse(line); } catch (e) { continue; }
    log('IN', line.slice(0, 300));
    const id = m.id;
    const method = m.method;
    if (method === 'initialize') {
      out({ jsonrpc: '2.0', id, result: {
        protocolVersion: (m.params && m.params.protocolVersion) || '2025-06-18',
        capabilities: { tools: { listChanged: true }, logging: {} },
        serverInfo: { name: 'relay-probe', version: VERSION },
        instructions: 'Relay probe server v' + VERSION + ', pid ' + process.pid + '.',
      }});
      continue;
    }
    if (method === 'notifications/initialized' || method === 'notifications/cancelled') continue;
    if (method === 'ping') { out({ jsonrpc: '2.0', id, result: {} }); continue; }
    if (method === 'tools/list') {
      out({ jsonrpc: '2.0', id, result: { tools: [{
        name: 'ping',
        description: 'Returns pong and the identity of the server process that answered.',
        inputSchema: { type: 'object', properties: { note: { type: 'string', description: 'Optional note echoed back.' } }, additionalProperties: false },
      }]}});
      log('OUT', 'tools/list');
      continue;
    }
    if (method === 'resources/list') { out({ jsonrpc: '2.0', id, result: { resources: [] } }); continue; }
    if (method === 'prompts/list') { out({ jsonrpc: '2.0', id, result: { prompts: [] } }); continue; }
    if (method === 'logging/setLevel') { out({ jsonrpc: '2.0', id, result: {} }); continue; }
    if (method === 'tools/call') {
      calls += 1;
      const note = (m.params && m.params.arguments && m.params.arguments.note) || '';
      const delay = Number(process.env.HO_CALL_DELAY_MS || 0);
      // Provoke a swap while THIS call is still unanswered, to measure what the
      // client sees when an in-flight request is interrupted by the apply.
      if (process.env.HO_SWAP_ON_CALL === '1' && calls === 1 && VERSION === '1.0.0') {
        try {
          fs.writeFileSync(path.join(__dirname, 'VERSION'), '2.0.0');
          if (process.env.HO_APPLY_DONE) fs.writeFileSync(process.env.HO_APPLY_DONE, 'done');
          if (process.env.HO_SWAP_REQUEST) fs.writeFileSync(process.env.HO_SWAP_REQUEST, 'now');
          log('SWAP-PROVOKED', 'requested a swap while call #1 is in flight');
        } catch (e) { log('SWAP-PROVOKE-FAILED', String(e)); }
      }
      const answer = () => {
        out({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text:
          'pong from RELAY-CHILD pid=' + process.pid + ' version=' + VERSION + ' call#' + calls + ' note=' + note }], isError: false } });
        log('OUT', 'tools/call #' + calls + ' answered');
      };
      if (delay > 0) setTimeout(answer, delay); else answer();
      continue;
    }
    if (id !== undefined) out({ jsonrpc: '2.0', id, error: { code: -32601, message: 'Method not found: ' + method } });
  }
});
process.stdin.on('end', () => { log('STDIN-END', ''); process.exit(0); });
