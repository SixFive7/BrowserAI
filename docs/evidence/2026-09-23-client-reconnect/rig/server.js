// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Tiny stdio MCP server for probing client behaviour when a server exits mid-session.
// Env:
//   PROBE_MODE      "stay" (default) | "exit-after-first"
//   PROBE_LOGDIR    directory for logs (required)
//   PROBE_TAG       label written into every line
const fs = require('fs');
const path = require('path');

const LOGDIR = process.env.PROBE_LOGDIR || '.';
const TAG = process.env.PROBE_TAG || 'probe';
const MODE = process.env.PROBE_MODE || 'stay';
const INSTANCE = `${TAG}-pid${process.pid}-${Date.now()}`;
fs.mkdirSync(LOGDIR, { recursive: true });
const LOG = path.join(LOGDIR, `${TAG}.launches.log`);

function log(kind, detail) {
  fs.appendFileSync(LOG, `${new Date().toISOString()} ${INSTANCE} ${kind} ${detail}\n`);
}
log('LAUNCH', `mode=${MODE} argv=${JSON.stringify(process.argv.slice(2))}`);
const COUNTER = path.join(LOGDIR, `${TAG}.launchcount`);
let launchNo = 1;
try { launchNo = (fs.existsSync(COUNTER) ? Number(fs.readFileSync(COUNTER, 'utf8')) : 0) + 1; } catch (e) {}
try { fs.writeFileSync(COUNTER, String(launchNo)); } catch (e) {}
log('LAUNCHNO', String(launchNo));
if (MODE.includes('die-on-launch:')) {
  const n = Number(MODE.split('die-on-launch:')[1].split('+')[0]);
  if (launchNo >= n) {
    process.stderr.write(`STDERR-${INSTANCE}: BrowserAI is applying a staged update right now; the server cannot start. Try again in a moment.
`);
    log('DIE-ON-LAUNCH', `launchNo=${launchNo} >= ${n}; exiting 1 before initialize`);
    process.exit(1);
  }
}
const EXITMARK = path.join(LOGDIR, `${TAG}.exited`);
process.on('exit', (c) => { try { log('EXIT', `code=${c}`); fs.appendFileSync(EXITMARK, `${INSTANCE} code=${c}
`); } catch (e) {} });
if (MODE.startsWith('exit-after-ms:')) {
  const ms = Number(MODE.split(':')[1]);
  setTimeout(() => {
    send({ jsonrpc: '2.0', method: 'notifications/message', params: { level: 'info', logger: 'browserai-update', data: FRIENDLY_IDLE } });
    process.stderr.write(`STDERR-${INSTANCE}: ${FRIENDLY_IDLE}
`);
    log('PLANNED-EXIT', `idle timer ${ms}ms`);
    setTimeout(() => process.exit(0), 200);
  }, ms).unref ? null : null;
}

let callCount = 0;
var FRIENDLY_PLACEHOLDER;
function send(obj) {
  const s = JSON.stringify(obj);
  process.stdout.write(s + '\n');
  log('OUT', s.slice(0, 600));
}

const FRIENDLY =
  `NOTIFY-${INSTANCE}: A BrowserAI update is staged and ready. No browser or session is open, ` +
  `so this MCP server is exiting now (code 0) to let the update apply. Please restart the ` +
  `BrowserAI MCP server, or start a new session, when you next need a browser.`;

const FRIENDLY_IDLE = FRIENDLY;
let buf = '';
process.stdin.on('data', (chunk) => {
  buf += chunk.toString('utf8');
  let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim();
    buf = buf.slice(nl + 1);
    if (!line) continue;
    let msg;
    try { msg = JSON.parse(line); } catch (e) { log('BADLINE', line.slice(0, 300)); continue; }
    log('IN', line.slice(0, 600));
    handle(msg);
  }
});
process.stdin.on('end', () => { log('STDIN-END', ''); process.exit(0); });

function handle(msg) {
  const { id, method } = msg;
  if (method === 'initialize') {
    send({ jsonrpc: '2.0', id, result: {
      protocolVersion: msg.params && msg.params.protocolVersion ? msg.params.protocolVersion : '2025-06-18',
      capabilities: { tools: { listChanged: true }, logging: {} },
      serverInfo: { name: 'probe', version: '0.0.1' },
      instructions: `Probe server instance ${INSTANCE}.`
    }});
    return;
  }
  if (method === 'notifications/initialized' || method === 'notifications/cancelled') return;
  if (method === 'ping') { send({ jsonrpc: '2.0', id, result: {} }); return; }
  if (method === 'tools/list') {
    send({ jsonrpc: '2.0', id, result: { tools: [{
      name: 'ping',
      description: 'Returns pong and the identity of the server process that answered.',
      inputSchema: { type: 'object', properties: { note: { type: 'string', description: 'Optional note echoed back.' } }, additionalProperties: false }
    }]}});
    return;
  }
  if (method === 'resources/list') { send({ jsonrpc: '2.0', id, result: { resources: [] } }); return; }
  if (method === 'prompts/list') { send({ jsonrpc: '2.0', id, result: { prompts: [] } }); return; }
  if (method === 'logging/setLevel') { send({ jsonrpc: '2.0', id, result: {} }); return; }
  if (method === 'tools/call') {
    callCount += 1;
    const n = callCount;
    const note = (msg.params && msg.params.arguments && msg.params.arguments.note) || '';
    const willExit = (MODE.includes('exit-after-first') || MODE.includes('crash-after-first')) && n === 1;
    const text = willExit
      ? `pong #${n} from ${INSTANCE}. note=${note}. RESULT-MARKER-EXITING: this server is about to exit for a staged update.`
      : `pong #${n} from ${INSTANCE}. note=${note}.`;
    send({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text }], isError: false } });
    if (willExit) {
      send({ jsonrpc: '2.0', method: 'notifications/message', params: { level: 'info', logger: 'browserai-update', data: FRIENDLY } });
      process.stderr.write(`STDERR-${INSTANCE}: ${FRIENDLY}\n`);
      log('PLANNED-EXIT', 'after first tools/call');
      setTimeout(() => process.exit(MODE.includes('crash-after-first') ? 3 : 0), 250);
    }
    return;
  }
  if (id !== undefined) send({ jsonrpc: '2.0', id, error: { code: -32601, message: `Method not found: ${method}` } });
}
