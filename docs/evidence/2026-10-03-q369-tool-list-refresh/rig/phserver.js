// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Q369 stand-in: a stdio MCP server that answers tools/list with ONE placeholder tool,
// "update_in_flight", while an "update" runs, and with the real list once it is over.
// Never BrowserAI itself. Every frame in both directions goes to <tag>.wire.jsonl with the
// launch number and pid, so "which process answered" is counted, not assumed.
//
// env:
//   PH_LOGDIR, PH_TAG             where <tag>.launches.log and <tag>.wire.jsonl go
//   PH_CAP                        listChanged (capabilities.tools.listChanged=true) | none ({"tools":{}})
//   PH_MODES                      per launch number, comma list, last entry repeats: placeholder | real
//   PH_DONE_FILE                  a placeholder launch becomes real when this file exists (polled every 100 ms)
//   PH_DONE_AFTER_CALL_MS         ...or this long after it answered its first tools/call (placeholder launches)
//   PH_NOTIFY_ON_DONE             1: send notifications/tools/list_changed at the moment it becomes real
//   PH_KILL_AFTER_CALL            N: the launches in PH_KILL_LAUNCHES terminate themselves 300 ms after
//                                 answering their Nth tools/call (TerminateProcess, exit code 1)
//   PH_KILL_FILE                  ...or when this file exists
//   PH_KILL_LAUNCHES              comma list of launch numbers the kill applies to (default 1)
//   PH_NOTIFY_AFTER_INIT          comma list of launch numbers that send list_changed after notifications/initialized
//   PH_NOTIFY_AFTER_INIT_DELAY_MS delay before that notification (default 0)
//   PH_INSTRUCTIONS               1: answer initialize with instructions (default 1)
'use strict';
const fs = require('fs');
const path = require('path');

const LOGDIR = process.env.PH_LOGDIR || '.';
const TAG = process.env.PH_TAG || 'ph';
const CAP = process.env.PH_CAP || 'listChanged';
const MODES = (process.env.PH_MODES || 'placeholder,real').split(',').map((s) => s.trim());
const DONE_FILE = process.env.PH_DONE_FILE || '';
const DONE_AFTER_CALL_MS = process.env.PH_DONE_AFTER_CALL_MS ? Number(process.env.PH_DONE_AFTER_CALL_MS) : null;
const NOTIFY_ON_DONE = process.env.PH_NOTIFY_ON_DONE === '1';
const KILL_AFTER_CALL = process.env.PH_KILL_AFTER_CALL ? Number(process.env.PH_KILL_AFTER_CALL) : null;
const KILL_FILE = process.env.PH_KILL_FILE || '';
const KILL_LAUNCHES = (process.env.PH_KILL_LAUNCHES || '1').split(',').map(Number);
const NOTIFY_AFTER_INIT = (process.env.PH_NOTIFY_AFTER_INIT || '').split(',').filter(Boolean).map(Number);
const NOTIFY_AFTER_INIT_DELAY_MS = Number(process.env.PH_NOTIFY_AFTER_INIT_DELAY_MS || 0);
const WITH_INSTRUCTIONS = (process.env.PH_INSTRUCTIONS || '1') === '1';
const STARTED = Date.now();

fs.mkdirSync(LOGDIR, { recursive: true });

// Race-safe launch number: the first launch of a run creates <tag>.launch.1, the next .2, ...
let launchNo = 0;
for (let n = 1; n < 1000; n++) {
  try {
    const fd = fs.openSync(path.join(LOGDIR, `${TAG}.launch.${n}`), 'wx');
    fs.writeSync(fd, `${new Date().toISOString()} pid=${process.pid}\n`);
    fs.closeSync(fd);
    launchNo = n;
    break;
  } catch (e) {
    if (e.code !== 'EEXIST') throw e;
  }
}
const modeAtLaunch = MODES[Math.min(launchNo, MODES.length) - 1] || 'real';
// A launch that starts after the update is done is the updated server: real from its first frame.
const doneAtLaunch = DONE_FILE !== '' && fs.existsSync(DONE_FILE);
let placeholder = modeAtLaunch === 'placeholder' && !doneAtLaunch;

const LAUNCHES = path.join(LOGDIR, `${TAG}.launches.log`);
const WIRE = path.join(LOGDIR, `${TAG}.wire.jsonl`);
const note = (s) => fs.appendFileSync(LAUNCHES, `${new Date().toISOString()} +${Date.now() - STARTED}ms launch=${launchNo} pid=${process.pid} ${s}\n`);
const wire = (direction, frame) => fs.appendFileSync(WIRE, JSON.stringify({ at: new Date().toISOString(), ms: Date.now() - STARTED, launch: launchNo, pid: process.pid, direction, frame }) + '\n');

note(`LAUNCH mode=${modeAtLaunch}${doneAtLaunch ? ' (the update was already done: REAL)' : ''} cap=${CAP} ppid=${process.ppid}`);

// ---- the texts a model would receive -------------------------------------------------
const PLACEHOLDER_DESCRIPTION =
  'BrowserAI is installing an update, so this tool list is a placeholder: the browser tools are not loaded yet. '
  + 'Call this tool to check on the update, and try again in about a minute. '
  + 'When the update is done, BrowserAI tells your client that its tools have changed, and the real tools replace this one.';
const PLACEHOLDER_RESULT =
  'BrowserAI is installing an update. No browser tool ran and nothing changed. '
  + 'Try again in about a minute. '
  + 'The tool list you have is a placeholder: when the update is done, BrowserAI tells your client to fetch the real tool list.';
const STALE_RESULT =
  'The BrowserAI update has finished, but your tool list is still the placeholder: your client has not fetched the new list. '
  + 'Reconnect the browserai server, or start a new session, to load the real tools.';
const PLACEHOLDER_INSTRUCTIONS =
  'PH-INSTRUCTIONS-PLACEHOLDER: BrowserAI is installing an update. Its tools are not loaded yet; call update_in_flight to check on it.';
const REAL_INSTRUCTIONS =
  'PH-INSTRUCTIONS-REAL: BrowserAI drives a real browser. Call browserai_init first.';

const PLACEHOLDER_TOOL = {
  name: 'update_in_flight',
  description: PLACEHOLDER_DESCRIPTION,
  inputSchema: { type: 'object', properties: {}, additionalProperties: false },
};
const REAL_TOOLS = [
  {
    name: 'browserai_init',
    description: 'Starts a BrowserAI session in a directory. (stub: names the launch that answered)',
    inputSchema: { type: 'object', properties: { directory: { type: 'string', description: 'An absolute directory.' } }, required: ['directory'], additionalProperties: false },
  },
  {
    name: 'browserai_list',
    description: 'Lists the BrowserAI sessions under a directory. (stub: names the launch that answered)',
    inputSchema: { type: 'object', properties: { directory: { type: 'string', description: 'An absolute directory.' } }, required: ['directory'], additionalProperties: false },
  },
];

function send(obj) {
  wire('server->client', obj);
  fs.writeSync(1, JSON.stringify(obj) + '\n');
}

function notifyListChanged(why) {
  note(`SEND notifications/tools/list_changed (${why})`);
  send({ jsonrpc: '2.0', method: 'notifications/tools/list_changed' });
}

function becomeReal(why) {
  if (!placeholder) return;
  placeholder = false;
  note(`UPDATE DONE (${why}): serving the real list from now on, in this process`);
  // Later launches start as the updated server.
  if (DONE_FILE && !fs.existsSync(DONE_FILE)) { try { fs.writeFileSync(DONE_FILE, `${new Date().toISOString()} written by launch ${launchNo}: ${why}
`); } catch (e) { /* ignore */ } }
  if (NOTIFY_ON_DONE) notifyListChanged('the update is done');
}

function die(why) {
  if (process.env.PH_KILL_MODE === 'clean') {
    // The control: a server that ends its own conversation (stdout closed, exit code 0).
    note(`ENDING ITSELF CLEANLY (${why}): stdout closed, exit code 0`);
    try { fs.closeSync(1); } catch (e) { /* ignore */ }
    setTimeout(() => process.exit(0), 50);
    return;
  }
  note(`TERMINATING ITSELF (${why}): TerminateProcess, exit code 1`);
  // process.kill(own pid, SIGKILL) is TerminateProcess on Windows: no exit handler runs and
  // the pipes close the way they do when an updater's kill pass ends the process.
  process.kill(process.pid, 'SIGKILL');
}

if (placeholder && DONE_FILE) {
  const t = setInterval(() => { if (fs.existsSync(DONE_FILE)) { clearInterval(t); becomeReal(`${DONE_FILE} appeared`); } }, 100);
}
if (KILL_FILE && KILL_LAUNCHES.includes(launchNo)) {
  const t = setInterval(() => { if (fs.existsSync(KILL_FILE)) { clearInterval(t); die(`${KILL_FILE} appeared`); } }, 100);
}

let calls = 0;
function handle(msg) {
  const { id, method } = msg;
  if (method === 'initialize') {
    if (process.env.PH_DIE_ON === 'initialize' && KILL_LAUNCHES.includes(launchNo)) {
      // Q369's hazard itself: the kill pass lands before the handshake has finished.
      die('on receiving initialize, before answering it');
      return;
    }
    const ci = msg.params && msg.params.clientInfo;
    note(`initialize from clientInfo=${JSON.stringify(ci)} protocolVersion=${msg.params && msg.params.protocolVersion} clientCaps=${JSON.stringify(msg.params && msg.params.capabilities)}`);
    const result = {
      protocolVersion: (msg.params && msg.params.protocolVersion) || '2025-06-18',
      capabilities: { tools: CAP === 'listChanged' ? { listChanged: true } : {} },
      serverInfo: { name: 'BrowserAI', version: placeholder ? '9.9.9-placeholder-stub' : '9.9.9-real-stub' },
    };
    if (WITH_INSTRUCTIONS) result.instructions = placeholder ? PLACEHOLDER_INSTRUCTIONS : REAL_INSTRUCTIONS;
    send({ jsonrpc: '2.0', id, result });
    return;
  }
  if (method === 'notifications/initialized') {
    if (NOTIFY_AFTER_INIT.includes(launchNo)) {
      setTimeout(() => notifyListChanged(`launch ${launchNo}, ${NOTIFY_AFTER_INIT_DELAY_MS} ms after notifications/initialized`), NOTIFY_AFTER_INIT_DELAY_MS);
    }
    return;
  }
  if (typeof method === 'string' && method.startsWith('notifications/')) return;
  if (method === 'ping') { send({ jsonrpc: '2.0', id, result: {} }); return; }
  if (method === 'tools/list') {
    const tools = placeholder ? [PLACEHOLDER_TOOL] : REAL_TOOLS;
    note(`tools/list answered with ${placeholder ? 'the PLACEHOLDER' : 'the REAL list'} (${tools.map((t) => t.name).join(',')})`);
    send({ jsonrpc: '2.0', id, result: { tools } });
    return;
  }
  if (method === 'resources/list') { send({ jsonrpc: '2.0', id, result: { resources: [] } }); return; }
  if (method === 'resources/templates/list') { send({ jsonrpc: '2.0', id, result: { resourceTemplates: [] } }); return; }
  if (method === 'prompts/list') { send({ jsonrpc: '2.0', id, result: { prompts: [] } }); return; }
  if (method === 'tools/call') {
    calls += 1;
    const tool = msg.params && msg.params.name;
    let result;
    if (tool === 'update_in_flight') {
      if (placeholder) {
        note(`tools/call #${calls} update_in_flight answered with the PLACEHOLDER text`);
        result = { content: [{ type: 'text', text: PLACEHOLDER_RESULT }], isError: false };
      } else {
        note(`tools/call #${calls} update_in_flight answered with the STALE text (the update is done)`);
        result = { content: [{ type: 'text', text: STALE_RESULT }], isError: true };
      }
    } else if (REAL_TOOLS.some((t) => t.name === tool)) {
      if (placeholder) {
        note(`tools/call #${calls} ${tool} REFUSED: still updating`);
        result = { content: [{ type: 'text', text: PLACEHOLDER_RESULT }], isError: true };
      } else {
        note(`tools/call #${calls} ${tool} SERVED`);
        result = { content: [{ type: 'text', text: `REAL-TOOL-SERVED launch=${launchNo} pid=${process.pid}: ${tool} found no sessions under ${msg.params.arguments && msg.params.arguments.directory}.` }], isError: false };
      }
    } else {
      note(`tools/call #${calls} ${tool} UNKNOWN`);
      result = { content: [{ type: 'text', text: `Unknown tool: ${tool}` }], isError: true };
    }
    send({ jsonrpc: '2.0', id, result });
    if (calls === 1 && placeholder && DONE_AFTER_CALL_MS !== null) {
      setTimeout(() => becomeReal(`${DONE_AFTER_CALL_MS} ms after the first call`), DONE_AFTER_CALL_MS);
    }
    if (KILL_AFTER_CALL !== null && calls === KILL_AFTER_CALL && KILL_LAUNCHES.includes(launchNo)) {
      setTimeout(() => die(`300 ms after answering call #${calls}`), 300);
    }
    return;
  }
  if (id !== undefined) send({ jsonrpc: '2.0', id, error: { code: -32601, message: `Method not found: ${method}` } });
}

let buf = '';
process.stdin.on('data', (chunk) => {
  buf += chunk.toString('utf8');
  let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim();
    buf = buf.slice(nl + 1);
    if (!line) continue;
    let msg;
    try { msg = JSON.parse(line); } catch (e) { wire('client->server-unparsed', line); continue; }
    wire('client->server', msg);
    try { handle(msg); } catch (e) { note(`HANDLER ERROR ${e && e.stack}`); }
  }
});
process.stdin.on('end', () => { note(`stdin EOF after ${Date.now() - STARTED} ms; exiting 0`); setTimeout(() => process.exit(0), 50); });
process.on('exit', (c) => { try { note(`process exit code=${c}`); } catch (e) { /* ignore */ } });
