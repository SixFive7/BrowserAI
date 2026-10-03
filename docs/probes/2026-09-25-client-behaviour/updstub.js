// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Q296 a stand-in: a stdio MCP server that behaves like a BrowserAI server that
// started while its install's Update.exe was running (UpdateInProgressServer):
// initialize answered normally, tools/list answered with a JSON-RPC error carrying
// the update sentence, every tools/call refused with the same sentence, and the
// conversation ended when the "updater" goes (here: a timer from process start).
// Every later launch in the same run is a NORMAL server with one tool.
//
// env:
//   US_LOGDIR, US_TAG            where the launch log and the full wire log go
//   US_MODE                      updating-first (default) | normal | updating-always
//   US_EXIT_AFTER_MS             an updating launch ends its conversation this long after it started (default 4000)
//   US_TOOL                      the one tool a normal launch lists (default browserai_list)
//
// The sentence is SessionErrors.UpdateIsBeingInstalled(tool, wasRunning:false, clientName)
// at src/BrowserAI/Sessions/SessionErrors.cs:193-212, composed per clientInfo.name the
// way KnownClients.Matches does (ordinal, case-insensitive).
'use strict';
const fs = require('fs');
const path = require('path');

const LOGDIR = process.env.US_LOGDIR || '.';
const TAG = process.env.US_TAG || 'updstub';
const MODE = process.env.US_MODE || 'updating-first';
const EXIT_AFTER_MS = Number(process.env.US_EXIT_AFTER_MS || 4000);
const TOOL = process.env.US_TOOL || 'browserai_list';
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

// updater-file: a launch is UPDATING when US_UPDATER_FILE exists at launch (the updater is
// running), and it ends its conversation when that file goes (the updater exited) -- the
// shape of Program.ServeWhileUpdatingAsync's updater.WhenExited, Program.cs:666-670.
const UPDATER_FILE = process.env.US_UPDATER_FILE || '';
// listed-first / listed-updater-file: the same, except that an updating launch answers
// tools/list with the real list -- the shape option b would give (Q296), measured so the
// conclusion about b rests on what the clients do with it and not on a reading of them.
// listed-serve-updater-file: option b's list at once, calls refused while the updater
// runs, and then -- instead of ending the conversation -- serving in the same process
// once the updater has gone (b with N91's deferred startup).
const LISTED = MODE === 'listed-first' || MODE === 'listed-updater-file' || MODE === 'listed-serve-updater-file';
const SERVE_AFTER = MODE === 'listed-serve-updater-file';
// held-updater-file: an updating launch HOLDS tools/list until the updater goes, then
// answers it with the real list and serves from then on (a deferred startup, N91's
// alternative b) -- measured for the same reason.
const HELD = MODE === 'held-updater-file';
// notify-updater-file: an updating launch answers tools/list with the ERROR (today's
// behaviour); when the updater goes it sends notifications/tools/list_changed and serves
// from then on, in the same process. Measures whether a client re-lists after a list
// that failed, when the server tells it the list changed.
const NOTIFY = MODE === 'notify-updater-file';
// q261-first: a side measurement of Q261's list-changed notification. Launch 1 serves and
// ends its conversation after its first answered tools/call; every later launch refuses the
// first tools/call that arrives before any tools/list on its connection, once, sending
// notifications/tools/list_changed ahead of the refusal (BrowserAI's Q261 order), and then
// serves. US_LIST_CHANGED_CAP decides whether tools.listChanged is declared.
const Q261 = MODE === 'q261-first';
let listedOnThisConnection = false;
let q261Refused = false;
let updating = MODE === 'updating-always'
  || ((MODE === 'updating-first' || MODE === 'listed-first') && launchNo === 1)
  || ((MODE === 'updater-file' || MODE === 'listed-updater-file' || MODE === 'held-updater-file' || MODE === 'notify-updater-file' || MODE === 'listed-serve-updater-file') && UPDATER_FILE.length > 0 && fs.existsSync(UPDATER_FILE));
const heldLists = [];
const LAUNCHES = path.join(LOGDIR, `${TAG}.launches.log`);
const WIRE = path.join(LOGDIR, `${TAG}.wire.jsonl`);
const note = (s) => fs.appendFileSync(LAUNCHES, `${new Date().toISOString()} launch=${launchNo} pid=${process.pid} ${s}\n`);
const wire = (direction, frame) => fs.appendFileSync(WIRE, JSON.stringify({ at: new Date().toISOString(), launch: launchNo, pid: process.pid, direction, frame }) + '\n');

note(`LAUNCH mode=${MODE} behaves=${updating ? (LISTED ? 'UPDATING-LISTED' : 'UPDATING') : 'NORMAL'} ppid=${process.ppid} argv=${JSON.stringify(process.argv.slice(2))}`);

let clientName = null;

function sentence(tool) {
  const known = (k) => typeof clientName === 'string' && clientName.length > 0 && clientName.toLowerCase() === k;
  const remedy = known('claude-code')
    ? 'Wait about a minute, then call again: your client starts the updated BrowserAI by itself on the next call.'
    : known('codex-mcp-client')
      ? 'Your client does not start a server again once it has gone, so after about a minute these tools need a new thread, or a reconnect of the BrowserAI server, before they answer.'
      : 'Wait about a minute, then call again. If your client then reports that the server has gone, reconnect the BrowserAI server.';
  return `BrowserAI is installing an update, so '${tool}' was NOT run: nothing reached a browser and nothing changed. `
    + remedy
    + ' A session you were using is closed by the update and not lost: its profile, files and log stay on disk, so call browserai_resume on its directory before you use it again.';
}

function sendList(id, why) {
  note(`tools/list answered with 1 tool${why}`);
  send({ jsonrpc: '2.0', id, result: { tools: [{
    name: TOOL,
    description: 'Lists the BrowserAI sessions under a directory. (stub: names the server launch that answered)',
    inputSchema: { type: 'object', properties: { directory: { type: 'string', description: 'An absolute directory.' } }, required: ['directory'], additionalProperties: false },
  }] } });
}

function send(obj) {
  wire('server->client', obj);
  process.stdout.write(JSON.stringify(obj) + '\n');
}

let ending = false;
function endTheConversation(why) {
  if (ending) return;
  ending = true;
  note(`ENDING the conversation: ${why}`);
  fs.writeFileSync(path.join(LOGDIR, `${TAG}.exited.${launchNo}`), `${new Date().toISOString()} pid=${process.pid} ${why}\n`);
  process.stdout.end(() => setTimeout(() => { note('EXIT code=0'); process.exit(0); }, 100));
}

if (updating && SERVE_AFTER) {
  const watch = setInterval(() => {
    if (!fs.existsSync(UPDATER_FILE)) {
      clearInterval(watch);
      updating = false;
      note(`the updater went (${Date.now() - STARTED} ms after start): serving from now on, in this process`);
      fs.writeFileSync(path.join(LOGDIR, `${TAG}.normal.${launchNo}`), new Date().toISOString());
    }
  }, 100);
} else if (updating && NOTIFY) {
  const watch = setInterval(() => {
    if (!fs.existsSync(UPDATER_FILE)) {
      clearInterval(watch);
      updating = false;
      note(`the updater went (${Date.now() - STARTED} ms after start): sending notifications/tools/list_changed and serving from now on`);
      send({ jsonrpc: '2.0', method: 'notifications/tools/list_changed' });
      fs.writeFileSync(path.join(LOGDIR, `${TAG}.normal.${launchNo}`), new Date().toISOString());
    }
  }, 100);
} else if (updating && HELD) {
  const watch = setInterval(() => {
    if (!fs.existsSync(UPDATER_FILE)) {
      clearInterval(watch);
      updating = false;
      note(`the updater went (${Date.now() - STARTED} ms after start): answering ${heldLists.length} held tools/list and serving from now on`);
      for (const id of heldLists.splice(0)) sendList(id, ' (held, answered after the updater went)');
    }
  }, 100);
} else if (updating && (MODE === 'updater-file' || MODE === 'listed-updater-file')) {
  const watch = setInterval(() => {
    if (!fs.existsSync(UPDATER_FILE)) { clearInterval(watch); endTheConversation(`the updater went (${UPDATER_FILE} removed, ${Date.now() - STARTED} ms after start)`); }
  }, 100);
} else if (updating) {
  setTimeout(() => endTheConversation(`the updater went (${EXIT_AFTER_MS} ms after start)`), EXIT_AFTER_MS);
}

function handle(msg) {
  const { id, method } = msg;
  if (method === 'initialize') {
    clientName = msg.params && msg.params.clientInfo ? msg.params.clientInfo.name : null;
    note(`initialize from clientInfo=${JSON.stringify(msg.params && msg.params.clientInfo)} protocolVersion=${msg.params && msg.params.protocolVersion}`);
    send({ jsonrpc: '2.0', id, result: {
      protocolVersion: (msg.params && msg.params.protocolVersion) || '2025-06-18',
      capabilities: { tools: process.env.US_LIST_CHANGED_CAP === '1' ? { listChanged: true } : {} },
      serverInfo: { name: 'BrowserAI', version: updating ? '9.9.9-updating-stub' : '9.9.9-normal-stub' },
      instructions: `UPDSTUB-INSTRUCTIONS launch=${launchNo} behaves=${updating ? 'UPDATING' : 'NORMAL'}: BrowserAI drives a real browser. Call ${TOOL} to list sessions.`,
    } });
    return;
  }
  if (typeof method === 'string' && method.startsWith('notifications/')) return;
  if (method === 'ping') { send({ jsonrpc: '2.0', id, result: {} }); return; }
  if (method === 'tools/list') {
    listedOnThisConnection = true;
    if (updating && HELD) {
      note('tools/list HELD until the updater goes');
      heldLists.push(id);
      return;
    }
    if (updating && !LISTED) {
      note('tools/list REFUSED with a JSON-RPC error');
      send({ jsonrpc: '2.0', id, error: { code: -32603, message: sentence('tools/list') } });
    } else {
      sendList(id, updating ? ' (UPDATING, option-b shape)' : '');
    }
    return;
  }
  if (method === 'tools/call') {
    const tool = msg.params && msg.params.name;
    if (updating) {
      note(`tools/call ${tool} REFUSED with the update sentence`);
      send({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text: sentence(tool) }], isError: true } });
    } else if (Q261 && launchNo > 1 && !listedOnThisConnection && !q261Refused) {
      q261Refused = true;
      note(`tools/call ${tool} REFUSED ONCE (Q261 shape): no tools/list on this connection; list_changed sent first`);
      send({ jsonrpc: '2.0', method: 'notifications/tools/list_changed' });
      send({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text: `Q261-STUB launch=${launchNo}: this server was started again since your tool list was read, so it may be stale. Call the tool again.` }], isError: true } });
    } else if (tool === TOOL) {
      note(`tools/call ${tool} SERVED`);
      if (Q261 && launchNo === 1) setTimeout(() => endTheConversation('Q261 shape: launch 1 ends after its first answered call'), 250);
      send({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text: `NORMAL-SERVER launch=${launchNo} pid=${process.pid}: no BrowserAI sessions under ${msg.params.arguments && msg.params.arguments.directory}.` }], isError: false } });
    } else {
      note(`tools/call ${tool} UNKNOWN`);
      send({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text: `unknown tool ${tool}` }], isError: true } });
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
    handle(msg);
  }
});
process.stdin.on('end', () => { note(`client closed stdin after ${Date.now() - STARTED} ms`); if (!ending) { ending = true; setTimeout(() => process.exit(0), 50); } });
process.on('exit', (c) => { try { note(`process exit code=${c} alive_ms=${Date.now() - STARTED}`); } catch (e) { /* ignore */ } });
