// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// What rig.mjs and replay.mjs share: the command line, the go file, an event log,
// an MCP client over a child's standard streams, the local page server, a port
// probe, Firefox's session files decoded, and a walk of a profile. Every file and
// socket operation here is asynchronous, and every wait is a timer.

import { spawn } from 'node:child_process';
import { createWriteStream } from 'node:fs';
import { access, cp, readdir, readFile, stat } from 'node:fs/promises';
import { createServer } from 'node:http';
import { connect } from 'node:net';
import { randomBytes } from 'node:crypto';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

export { delay };

export const iso = () => new Date().toISOString();

// --name value pairs, and nothing else.
export function parseArgs(argv) {
  const values = {};
  for (let index = 0; index < argv.length; index += 2) {
    const key = argv[index];
    if (!key.startsWith('--') || index + 1 >= argv.length) throw new Error(`argument ${index}: expected --name value, got ${key}`);
    values[key.slice(2)] = argv[index + 1];
  }
  return values;
}

export function required(values, ...names) {
  for (const name of names) if (values[name] === undefined) throw new Error(`--${name} is required`);
  return values;
}

// The launcher writes the go file once this process is in the step's job; nothing is
// started before it exists, so every child is born inside that job.
export async function waitForFile(path, limitMs = 120000) {
  const started = Date.now();
  for (;;) {
    try { await access(path); return; } catch { /* not yet */ }
    if (Date.now() - started > limitMs) throw new Error(`${path} did not appear within ${limitMs} ms`);
    await delay(100);
  }
}

export class EventLog {
  constructor(path) {
    this.stream = createWriteStream(path, { flags: 'a' });
    this.stream.on('error', (error) => process.stderr.write(`event log: ${error}\n`));
  }
  write(event, data = {}) {
    const line = JSON.stringify({ t: iso(), event, ...data });
    this.stream.write(`${line}\n`);
    process.stdout.write(`${line.slice(0, 400)}\n`);
  }
  close() { return new Promise((resolve) => this.stream.end(resolve)); }
}

// One JSON object per line, as MCP's stdio transport frames them.
export class McpClient {
  constructor(command, args, { cwd, env, stderrPath, log, label }) {
    this.label = label;
    this.log = log;
    this.child = spawn(command, args, { cwd, env, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    this.pid = this.child.pid;
    this.spawnError = null;
    this.child.once('error', (error) => { this.spawnError = String(error); });
    this.child.stdin.on('error', (error) => this.log?.write('stdin-error', { from: this.label, error: String(error) }));
    // 'close' and not 'exit': it comes once the child's streams have ended too.
    this.closed = new Promise((resolve) => this.child.once('close', (code, signal) => resolve({ code, signal })));
    this.stderr = createWriteStream(stderrPath, { flags: 'a' });
    this.stderr.on('error', (error) => this.log?.write('stderr-file-error', { from: this.label, error: String(error) }));
    this.child.stderr.pipe(this.stderr);
    this.waiting = new Map();
    this.nextId = 1;
    let pending = '';
    this.child.stdout.setEncoding('utf8');
    this.child.stdout.on('data', (chunk) => {
      pending += chunk;
      for (let end = pending.indexOf('\n'); end >= 0; end = pending.indexOf('\n')) {
        const line = pending.slice(0, end).trim();
        pending = pending.slice(end + 1);
        if (line) this.onLine(line);
      }
    });
  }

  onLine(line) {
    let message;
    try { message = JSON.parse(line); } catch { this.log?.write('unparsed', { from: this.label, line: line.slice(0, 500) }); return; }
    const isAnswer = message.id !== undefined && (message.result !== undefined || message.error !== undefined);
    if (isAnswer && this.waiting.has(message.id)) {
      const settle = this.waiting.get(message.id);
      this.waiting.delete(message.id);
      settle(message);
      return;
    }
    if (message.method && message.id !== undefined) {
      // A request from the server. A ping is answered so that a heartbeat never ends
      // the connection, and anything else is told it is not offered.
      const reply = message.method === 'ping'
        ? { jsonrpc: '2.0', id: message.id, result: {} }
        : { jsonrpc: '2.0', id: message.id, error: { code: -32601, message: 'not offered by this client' } };
      this.child.stdin.write(`${JSON.stringify(reply)}\n`);
      this.log?.write('server-request', { from: this.label, method: message.method });
      return;
    }
    if (message.method) this.log?.write('notification', { from: this.label, method: message.method });
  }

  request(method, params, timeoutMs) {
    const id = this.nextId++;
    return new Promise((resolve) => {
      const timer = setTimeout(() => { this.waiting.delete(id); resolve({ timedOut: true }); }, timeoutMs);
      this.waiting.set(id, (message) => { clearTimeout(timer); resolve(message); });
      this.child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
    });
  }

  notify(method, params) {
    this.child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method, ...(params ? { params } : {}) })}\n`);
  }

  async handshake(protocolVersion, name, timeoutMs = 60000) {
    const opened = await this.request('initialize', { protocolVersion, capabilities: {}, clientInfo: { name, version: '1' } }, timeoutMs);
    if (opened.timedOut || opened.error) throw new Error(`${this.label}: initialize was not answered: ${JSON.stringify(opened).slice(0, 400)}`);
    this.notify('notifications/initialized');
    const listed = await this.request('tools/list', {}, timeoutMs);
    if (listed.timedOut || listed.error) throw new Error(`${this.label}: tools/list was not answered: ${JSON.stringify(listed).slice(0, 400)}`);
    return { server: opened.result?.serverInfo ?? null, protocolVersion: opened.result?.protocolVersion ?? null, tools: (listed.result?.tools ?? []).map((tool) => tool.name) };
  }

  // One tool call, timed from the client's side.
  async call(name, args, timeoutMs) {
    const startedAt = iso();
    const started = Date.now();
    const message = await this.request('tools/call', { name, arguments: args }, timeoutMs);
    const ms = Date.now() - started;
    const answeredAt = iso();
    if (message.timedOut) {
      this.log?.write('call', { from: this.label, tool: name, ms, outcome: `no answer within ${timeoutMs} ms` });
      return { tool: name, startedAt, answeredAt, ms, timedOut: true, isError: true, text: '' };
    }
    if (message.error) {
      const text = `JSON-RPC error ${message.error.code}: ${message.error.message}`;
      this.log?.write('call', { from: this.label, tool: name, ms, outcome: text.slice(0, 200) });
      return { tool: name, startedAt, answeredAt, ms, rpcError: message.error, isError: true, text };
    }
    const content = message.result?.content ?? [];
    const text = content.filter((part) => part.type === 'text').map((part) => part.text).join('\n');
    const isError = message.result?.isError === true;
    this.log?.write('call', { from: this.label, tool: name, ms, isError, text: text.slice(0, 200).replace(/\s+/g, ' ') });
    return { tool: name, startedAt, answeredAt, ms, isError, text };
  }

  // The end every MCP server is told first: its input closes. One that is still running
  // after the grace is left to the step's job, which ends it with everything else.
  async end(graceMs = 30000) {
    try { this.child.stdin.end(); } catch { /* already gone */ }
    const ended = await Promise.race([this.closed, delay(graceMs).then(() => null)]);
    if (!ended) this.child.stderr.unpipe(this.stderr);
    if (!this.stderr.writableFinished && !this.stderr.destroyed) {
      await new Promise((resolve) => {
        this.stderr.once('finish', resolve);
        this.stderr.once('close', resolve);
        if (!this.stderr.writableEnded) this.stderr.end();
      });
    }
    return ended ?? { stillRunning: true };
  }
}

export const isStall = (answer) => !!answer && answer.isError === true && /Timeout 180000ms exceeded/.test(answer.text ?? '');

export function callLogOf(text) {
  const at = (text ?? '').indexOf('Call log:');
  return at < 0 ? null : text.slice(at);
}

// The answer headers of the dashboard the 2026-10-12 sessions had open: the values of
// PageListener.SecurityHeaders at 449fc340, copied and not shared.
const DASHBOARD_HEADERS = {
  'Cache-Control': 'no-store',
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'no-referrer',
  'Cross-Origin-Opener-Policy': 'same-origin',
  'Cross-Origin-Resource-Policy': 'same-origin',
  'X-Frame-Options': 'DENY',
  'Content-Security-Policy': "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; form-action 'none'; frame-ancestors 'none'; base-uri 'none'",
};

// The answers a page can be served with. 'nostore' and 'cacheable' were added after the
// first batch, whose plain page came back from Firefox's cache with its server gone: the
// one dashboard header that decides whether the cache may keep a page, alone, and every
// dashboard header but that one.
const HEADER_SETS = {
  dashboard: DASHBOARD_HEADERS,
  plain: {},
  nostore: { 'Cache-Control': DASHBOARD_HEADERS['Cache-Control'] },
  cacheable: Object.fromEntries(Object.entries(DASHBOARD_HEADERS).filter(([name]) => name !== 'Cache-Control')),
};

const PAGE_HTML = `<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>restore-stall page</title><link rel="stylesheet" href="page.css"></head>
<body data-tab="1" data-page="home"><main id="main"><h1>restore-stall</h1><p>seconds <span id="tick">0</span>, events <span id="events">0</span></p></main><script src="page.js"></script></body>
</html>
`;

const PAGE_CSS = 'body { font: 16px sans-serif; margin: 40px; }\n';

// A ticking clock and a server-sent event stream, as the dashboard's script has.
const PAGE_JS = `'use strict';
(() => {
  const tick = document.getElementById('tick');
  const seen = document.getElementById('events');
  let seconds = 0;
  let events = 0;
  setInterval(() => { tick.textContent = String(++seconds); }, 1000);
  const stream = new EventSource('events?tab=1&page=home');
  stream.onmessage = () => { seen.textContent = String(++events); };
})();
`;

function listen(server, port) {
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, '127.0.0.1', () => { server.off('error', reject); resolve(server.address().port); });
  });
}

// A page on 127.0.0.1 shaped like the one the sightings were on: a token in the path,
// '?tab=1', an external script and style sheet, a clock and an event stream, served with
// one of the header sets above.
export async function startPageServer({ headers = 'dashboard', port = 0, token } = {}) {
  const pathToken = token ?? randomBytes(32).toString('base64url');
  const base = `/${pathToken}/`;
  const extra = HEADER_SETS[headers];
  if (!extra) throw new Error(`unknown header set ${headers}`);
  const streams = new Set();
  const counts = { requests: 0, sseOpened: 0, last: [] };
  const server = createServer((request, response) => {
    const url = new URL(request.url, 'http://127.0.0.1');
    counts.requests++;
    counts.last = [...counts.last, `${iso()} ${request.method} ${url.pathname}${url.search}`].slice(-8);
    const send = (status, type, body) => {
      response.writeHead(status, { 'Content-Type': type, 'Content-Length': Buffer.byteLength(body), ...extra });
      response.end(body);
    };
    if (url.pathname === base) return send(200, 'text/html; charset=utf-8', PAGE_HTML);
    if (url.pathname === `${base}page.css`) return send(200, 'text/css; charset=utf-8', PAGE_CSS);
    if (url.pathname === `${base}page.js`) return send(200, 'text/javascript; charset=utf-8', PAGE_JS);
    if (url.pathname === `${base}events`) {
      counts.sseOpened++;
      response.writeHead(200, { 'Content-Type': 'text/event-stream', ...extra });
      response.write(': open\n\n');
      let sent = 0;
      const entry = { response, timer: setInterval(() => response.write(`data: ${++sent}\n\n`), 1000) };
      streams.add(entry);
      request.on('close', () => { clearInterval(entry.timer); streams.delete(entry); });
      return undefined;
    }
    return send(404, 'text/plain; charset=utf-8', 'not here\n');
  });
  const bound = await listen(server, port);
  let closing = null;
  return {
    port: bound,
    token: pathToken,
    url: `http://127.0.0.1:${bound}${base}?tab=1`,
    stats: () => ({ requests: counts.requests, sseOpened: counts.sseOpened, sseOpen: streams.size, last: counts.last }),
    // Once: a second call waits for the first.
    close: () => {
      closing ??= (async () => {
        for (const entry of streams) { clearInterval(entry.timer); entry.response.destroy(); }
        streams.clear();
        server.closeAllConnections();
        await new Promise((resolve) => server.close(() => resolve()));
      })();
      return closing;
    },
  };
}

// Whether anything answers on a loopback port: 'refused' is the state the sightings had.
export function probePort(port, timeoutMs = 10000) {
  return new Promise((resolve) => {
    const started = Date.now();
    const socket = connect({ host: '127.0.0.1', port });
    let settled = false;
    const done = (state, code) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      socket.destroy();
      resolve({ state, code: code ?? null, ms: Date.now() - started });
    };
    const timer = setTimeout(() => done('no answer'), timeoutMs);
    socket.once('connect', () => done('open'));
    socket.once('error', (error) => done(error.code === 'ECONNREFUSED' ? 'refused' : 'error', error.code));
  });
}

// The uninstall hook's way of ending a background: 'browserai/stop' on its pipe, framed the
// way BackgroundConversation frames it at 449fc340, one JSON line each way. The background
// answers, closes every session it holds and ends.
export function askBackgroundToStop(pipePath, boundMs = 15000) {
  return new Promise((resolve) => {
    const socket = connect(pipePath);
    let answer = '';
    let settled = false;
    const done = (outcome) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      socket.destroy();
      resolve(outcome);
    };
    const timer = setTimeout(() => done({ answered: false, reason: `no answer within ${boundMs} ms` }), boundMs);
    socket.setEncoding('utf8');
    socket.once('connect', () => socket.write(`${JSON.stringify({ jsonrpc: '2.0', id: 'browserai-start-1', method: 'browserai/stop', params: {} })}\n`));
    socket.on('data', (chunk) => {
      answer += chunk;
      if (answer.includes('\n')) done({ answered: true, line: answer.trim() });
    });
    socket.once('error', (error) => done({ answered: false, reason: String(error) }));
  });
}

// mozLz40: the magic, the decompressed length, then one LZ4 block.
export function decodeMozLz4(buffer) {
  if (buffer.subarray(0, 8).toString('latin1') !== 'mozLz40\0') throw new Error('not a mozLz40 file');
  const length = buffer.readUInt32LE(8);
  const source = buffer.subarray(12);
  const output = Buffer.alloc(length);
  let at = 0;
  let to = 0;
  while (at < source.length) {
    const token = source[at++];
    let literals = token >> 4;
    if (literals === 15) { let next; do { next = source[at++]; literals += next; } while (next === 255); }
    source.copy(output, to, at, at + literals);
    at += literals;
    to += literals;
    if (at >= source.length) break;
    const offset = source[at] | (source[at + 1] << 8);
    at += 2;
    let match = token & 15;
    if (match === 15) { let next; do { next = source[at++]; match += next; } while (next === 255); }
    match += 4;
    for (let copied = 0; copied < match; copied++) { output[to] = output[to - offset]; to++; }
  }
  return JSON.parse(output.subarray(0, to).toString('utf8'));
}

const FIREFOX_SESSION_FILES = [
  'sessionstore.jsonlz4',
  'sessionstore-backups/recovery.jsonlz4',
  'sessionstore-backups/recovery.baklz4',
  'sessionstore-backups/previous.jsonlz4',
];

// What each of Firefox's session files would restore: every window's tabs, each tab's
// history and which entry it is on, and the session's own state.
export async function readFirefoxSession(profile) {
  const files = [];
  for (const relative of FIREFOX_SESSION_FILES) {
    const path = join(profile, ...relative.split('/'));
    let buffer;
    try { buffer = await readFile(path); } catch { continue; }
    try {
      const session = decodeMozLz4(buffer);
      files.push({
        file: relative,
        bytes: buffer.length,
        state: session.session?.state ?? null,
        windows: (session.windows ?? []).map((window) => ({
          selected: window.selected ?? null,
          // Firefox 143 and later keep an entry's policy container, its content security
          // policy inside it, where 142 and earlier kept 'csp' (SessionHistory.sys.mjs).
          tabs: (window.tabs ?? []).map((tab) => ({
            index: tab.index ?? null,
            entries: (tab.entries ?? []).map((entry) => entry.url),
            policies: (tab.entries ?? []).map((entry) => (entry.policyContainer !== undefined ? 'policyContainer' : entry.csp !== undefined ? 'csp' : null)),
          })),
        })),
        closedWindows: (session._closedWindows ?? []).length,
      });
    } catch (error) {
      files.push({ file: relative, bytes: buffer.length, error: String(error) });
    }
  }
  return files;
}

// Chromium's session files are binary; what is read is which of them carry the page's token.
export async function readChromiumSession(profile, token) {
  const folder = join(profile, 'Default', 'Sessions');
  let names = [];
  try { names = await readdir(folder); } catch { return { folder: 'Default/Sessions', missing: true }; }
  const files = [];
  for (const name of names) {
    // A running Chromium holds its current session file open with no sharing; that file
    // is named and not read, which the first batch's Chromium rows lost an outcome to.
    let buffer;
    try { buffer = await readFile(join(folder, name)); } catch (error) { files.push({ name, unread: error.code ?? String(error) }); continue; }
    files.push({ name, bytes: buffer.length, hasToken: buffer.includes(Buffer.from(token, 'latin1')) || buffer.includes(Buffer.from(token, 'utf16le')) });
  }
  return { folder: 'Default/Sessions', files };
}

// How many files a tree holds and how many bytes, reading through files a browser holds open.
export async function measureTree(root) {
  let files = 0;
  let bytes = 0;
  let unreadable = 0;
  const walk = async (folder) => {
    let entries;
    try { entries = await readdir(folder, { withFileTypes: true }); } catch { unreadable++; return; }
    for (const entry of entries) {
      const path = join(folder, entry.name);
      if (entry.isDirectory()) { await walk(path); continue; }
      try { bytes += (await stat(path)).size; files++; } catch { unreadable++; }
    }
  };
  await walk(root);
  return { files, bytes, unreadable };
}

export async function copyTree(from, to) {
  await cp(from, to, { recursive: true, errorOnExist: true, force: false });
}

// The block BrowserAI starts a session's child with: ChildEnvironment.InheritedWhenSet and
// .Forced at 449fc340, copied and not shared. Windows reads names without regard to case.
const INHERITED = [
  'SystemRoot', 'windir', 'SystemDrive', 'COMSPEC', 'PATH', 'PATHEXT',
  'NUMBER_OF_PROCESSORS', 'PROCESSOR_ARCHITECTURE', 'PROCESSOR_IDENTIFIER', 'OS',
  'TEMP', 'TMP', 'USERPROFILE', 'LOCALAPPDATA', 'APPDATA', 'HOMEDRIVE', 'HOMEPATH',
  'PUBLIC', 'ProgramData', 'ALLUSERSPROFILE', 'ProgramFiles', 'ProgramFiles(x86)',
  'ProgramW6432', 'CommonProgramFiles', 'CommonProgramFiles(x86)', 'CommonProgramW6432',
  'USERNAME', 'USERDOMAIN', 'COMPUTERNAME', 'SESSIONNAME',
  'HTTP_PROXY', 'HTTPS_PROXY', 'NO_PROXY', 'ALL_PROXY', 'NODE_EXTRA_CA_CERTS',
  'PWTEST_SERVER_REGISTRY',
];

const FORCED = { PLAYWRIGHT_SKIP_BROWSER_GC: '1', PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1', MOZ_DISABLE_SAFE_MODE_KEY: '1' };

export function childEnvironment(base, additions) {
  const byUpper = new Map(Object.keys(base).map((name) => [name.toUpperCase(), name]));
  const block = {};
  for (const name of INHERITED) {
    const actual = byUpper.get(name.toUpperCase());
    if (actual !== undefined && base[actual] !== undefined) block[name] = base[actual];
  }
  return { ...block, ...FORCED, ...additions };
}

// What the rig hands BrowserAI's own two processes: this process's block without the
// variables that tell a client who it is, so the relay serves a client of no known kind.
export function browserAiEnvironment(base, additions) {
  const block = {};
  for (const [name, value] of Object.entries(base)) {
    if (/^(CLAUDE|CODEX|VSCODE|ANTHROPIC|OPENAI)/i.test(name)) continue;
    block[name] = value;
  }
  return { ...block, ...additions };
}

export const normalisePath = (path) => String(path).replace(/^\\\\\?\\/, '').replace(/\//g, '\\').replace(/\\+$/, '').toLowerCase();
