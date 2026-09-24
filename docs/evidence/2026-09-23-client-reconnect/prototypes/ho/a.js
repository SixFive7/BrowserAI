// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr


// A: the server the client spawned. Lives in the "install folder".
// On HO_TRIGGER it writes its negotiated MCP state to a file, spawns H with A's
// own stdio handles inherited, stops reading stdin, and exits.
const fs = require('fs');
const { spawnSync, spawn } = require('child_process');
const { mkLog, out, toolsList } = require('./common.js');

const LOGDIR = process.env.HO_LOGDIR;
const TAG = process.env.HO_TAG || 'ho';
const ROLE = 'A';
const TRIGGER = process.env.HO_TRIGGER || 'none';   // none | after-first-call | on-initialize
const STATE = process.env.HO_STATE;
const NODE = process.execPath;
const HDIR = process.env.HO_HELPER_DIR;             // where H and B live (OUTSIDE the install folder)
const log = mkLog(LOGDIR, TAG);
log('LAUNCH', `role=${ROLE} trigger=${TRIGGER}`);
process.on('exit', (c) => { try { log('EXIT', `role=${ROLE} code=${c}`); } catch (e) {} });

let negotiated = null;
let calls = 0;
let handedOver = false;
let buf = '';

function handover(residual) {
  handedOver = true;
  const state = {
    handedOverFrom: process.pid,
    at: new Date().toISOString(),
    initializeDone: true,
    negotiated,                 // protocolVersion, client capabilities, clientInfo
    callCount: calls,
    residual,                   // bytes A had read but not acted on
  };
  fs.writeFileSync(STATE, JSON.stringify(state));
  log('HANDOVER-STATE', `wrote ${STATE} residual=${JSON.stringify(residual).slice(0, 120)}`);
  const h = spawn(NODE, [HDIR + '/h.js'], {
    stdio: ['inherit', 'inherit', 'inherit'],   // H inherits A's pipes to the client
    env: process.env,
    windowsHide: true,
    detached: true,            // MUST: a non-detached child dies with A's job object
  });
  h.unref();
  log('HANDOVER-SPAWNED', `helper pid=${h.pid} with inherited stdio`);
  try { process.stdin.pause(); process.stdin.destroy(); } catch (e) {}
  setTimeout(() => { log('HANDOVER-EXIT', 'A exiting 0; the pipe is now held by H'); process.exit(0); }, 120);
}

process.stdin.on('data', (chunk) => {
  if (handedOver) { log('LATE-STDIN', `A saw ${chunk.length}B after handover (dropped)`); return; }
  buf += chunk.toString('utf8');
  let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim();
    buf = buf.slice(nl + 1);
    if (!line) continue;
    let msg; try { msg = JSON.parse(line); } catch (e) { log('BADLINE', line.slice(0, 200)); continue; }
    log('IN', line.slice(0, 400));
    handle(msg);
    if (handedOver) return;
  }
});
process.stdin.on('end', () => { if (!handedOver) { log('STDIN-END', ''); process.exit(0); } });

function handle(msg) {
  const { id, method } = msg;
  if (method === 'initialize') {
    negotiated = {
      protocolVersion: (msg.params && msg.params.protocolVersion) || '2025-06-18',
      clientCapabilities: (msg.params && msg.params.capabilities) || {},
      clientInfo: (msg.params && msg.params.clientInfo) || {},
    };
    out({ jsonrpc: '2.0', id, result: {
      protocolVersion: negotiated.protocolVersion,
      capabilities: { tools: { listChanged: true }, logging: {} },
      serverInfo: { name: 'handover-probe', version: 'A' },
      instructions: `Handover probe, role A, pid ${process.pid}.`,
    }});
    log('OUT', `initialize result (protocolVersion=${negotiated.protocolVersion})`);
    if (TRIGGER === 'on-initialize') { handover(buf); }
    return;
  }
  if (method === 'notifications/initialized' || method === 'notifications/cancelled') return;
  if (method === 'ping') { out({ jsonrpc: '2.0', id, result: {} }); return; }
  if (method === 'tools/list') { out({ jsonrpc: '2.0', id, result: { tools: toolsList() } }); log('OUT', 'tools/list'); return; }
  if (method === 'resources/list') { out({ jsonrpc: '2.0', id, result: { resources: [] } }); return; }
  if (method === 'prompts/list') { out({ jsonrpc: '2.0', id, result: { prompts: [] } }); return; }
  if (method === 'logging/setLevel') { out({ jsonrpc: '2.0', id, result: {} }); return; }
  if (method === 'tools/call') {
    calls += 1;
    const note = (msg.params && msg.params.arguments && msg.params.arguments.note) || '';
    out({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text:
      `pong from ROLE=A pid=${process.pid} call#${calls} note=${note}` }], isError: false } });
    log('OUT', `tools/call #${calls} answered by A`);
    if (TRIGGER === 'after-first-call' && calls === 1) { handover(buf); }
    return;
  }
  if (id !== undefined) out({ jsonrpc: '2.0', id, error: { code: -32601, message: `Method not found: ${method}` } });
}
