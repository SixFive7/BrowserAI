// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr


// B: the post-update server. Started by H with the client's pipes inherited.
// It knows initialize already happened, so it never asks for one.
const fs = require('fs');
const { mkLog, out, toolsList } = require('./common.js');
const LOGDIR = process.env.HO_LOGDIR;
const TAG = process.env.HO_TAG || 'ho';
const STATE = process.env.HO_STATE;
const ANNOUNCE = process.env.HO_ANNOUNCE_LIST_CHANGED === '1';
const log = mkLog(LOGDIR, TAG);
const state = JSON.parse(fs.readFileSync(STATE, 'utf8'));
let VERSION = 'unknown';
try { VERSION = fs.readFileSync(__dirname + '/VERSION', 'utf8').trim(); } catch (e) {}
log('LAUNCH', `role=B adopting state from pid=${state.handedOverFrom} initializeDone=${state.initializeDone} protocolVersion=${state.negotiated && state.negotiated.protocolVersion} callCount=${state.callCount}`);
process.on('exit', (c) => { try { log('EXIT', `role=B code=${c}`); } catch (e) {} });

let calls = state.callCount || 0;
let buf = state.residual || '';
if (buf) log('RESIDUAL', `replaying ${buf.length}B A had already read`);

if (ANNOUNCE) {
  out({ jsonrpc: '2.0', method: 'notifications/tools/list_changed', params: {} });
  log('OUT', 'notifications/tools/list_changed announced by B');
}

function pump() {
  let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim();
    buf = buf.slice(nl + 1);
    if (!line) continue;
    let msg; try { msg = JSON.parse(line); } catch (e) { log('BADLINE', line.slice(0, 200)); continue; }
    log('IN', line.slice(0, 400));
    handle(msg);
  }
}
pump();
process.stdin.on('data', (chunk) => { buf += chunk.toString('utf8'); pump(); });
process.stdin.on('end', () => { log('STDIN-END', ''); process.exit(0); });

function handle(msg) {
  const { id, method } = msg;
  if (method === 'initialize') {
    // Should never happen in the handover shape; answer anyway and record it loudly.
    log('UNEXPECTED-INITIALIZE', 'the client re-initialized after the handover');
    out({ jsonrpc: '2.0', id, result: {
      protocolVersion: (msg.params && msg.params.protocolVersion) || state.negotiated.protocolVersion,
      capabilities: { tools: { listChanged: true }, logging: {} },
      serverInfo: { name: 'handover-probe', version: 'B' },
      instructions: `Handover probe, role B, pid ${process.pid}.`,
    }});
    return;
  }
  if (method === 'notifications/initialized' || method === 'notifications/cancelled') return;
  if (method === 'ping') { out({ jsonrpc: '2.0', id, result: {} }); return; }
  if (method === 'tools/list') { out({ jsonrpc: '2.0', id, result: { tools: toolsList() } }); log('OUT', 'tools/list answered by B'); return; }
  if (method === 'resources/list') { out({ jsonrpc: '2.0', id, result: { resources: [] } }); return; }
  if (method === 'prompts/list') { out({ jsonrpc: '2.0', id, result: { prompts: [] } }); return; }
  if (method === 'logging/setLevel') { out({ jsonrpc: '2.0', id, result: {} }); return; }
  if (method === 'tools/call') {
    calls += 1;
    const note = (msg.params && msg.params.arguments && msg.params.arguments.note) || '';
    out({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text:
      `pong from ROLE=B pid=${process.pid} version=${VERSION} call#${calls} note=${note} (post-update, no re-initialize)` }], isError: false } });
    log('OUT', `tools/call #${calls} answered by B`);
    return;
  }
  if (id !== undefined) out({ jsonrpc: '2.0', id, error: { code: -32601, message: `Method not found: ${method}` } });
}
