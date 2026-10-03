// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// lib.js -- shared pieces of the state-across-close rig.
// Drives a real @playwright/mcp child over stdio the way BrowserAI does:
// node.exe cli.js --config <file> --sandbox, cwd = the session directory,
// an allowlisted environment, one job object per child.
'use strict';
const { spawn } = require('node:child_process');
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const ROOT = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\state-across-close';
const REPO = 'C:\\Source\\SixFive7\\BrowserAI';
const NODE = path.join(REPO, 'payload', 'node', 'node.exe');
const BROWSERS = path.join(REPO, '.work', 'browsers-cache');
const CLI = {
  '082': path.join(REPO, 'payload', 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js'),
  '083': path.join(ROOT, 'mcp083', 'node_modules', '@playwright', 'mcp', 'cli.js'),
};
const LOCALE = 'nl-NL';           // CultureInfo.CurrentCulture.Name on this machine, read 2026-10-03
const TIMEZONE = 'Europe/Berlin'; // TryConvertWindowsIdToIanaId("W. Europe Standard Time"), read 2026-10-03

const wait = ms => new Promise(r => setTimeout(r, ms));
const now = () => Number(process.hrtime.bigint()) / 1e6;

// ---------------------------------------------------------------- environment
// ChildEnvironment.InheritedWhenSet + Forced, with the four isolation overrides.
const INHERITED = ['SystemRoot', 'windir', 'SystemDrive', 'COMSPEC', 'PATH', 'PATHEXT',
  'NUMBER_OF_PROCESSORS', 'PROCESSOR_ARCHITECTURE', 'PROCESSOR_IDENTIFIER', 'OS',
  'TEMP', 'TMP', 'USERPROFILE', 'LOCALAPPDATA', 'APPDATA', 'HOMEDRIVE', 'HOMEPATH',
  'PUBLIC', 'ProgramData', 'ALLUSERSPROFILE', 'ProgramFiles', 'ProgramFiles(x86)',
  'ProgramW6432', 'CommonProgramFiles', 'CommonProgramFiles(x86)', 'CommonProgramW6432',
  'USERNAME', 'USERDOMAIN', 'COMPUTERNAME', 'SESSIONNAME',
  'HTTP_PROXY', 'HTTPS_PROXY', 'NO_PROXY', 'ALL_PROXY', 'NODE_EXTRA_CA_CERTS',
  'PWTEST_SERVER_REGISTRY'];

function childEnv() {
  const src = {};
  for (const [k, v] of Object.entries(process.env)) src[k.toUpperCase()] = [k, v];
  const env = {};
  for (const name of INHERITED) {
    const hit = src[name.toUpperCase()];
    if (hit) env[name] = hit[1];
  }
  env.PLAYWRIGHT_SKIP_BROWSER_GC = '1';
  env.PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = '1';
  env.PLAYWRIGHT_BROWSERS_PATH = BROWSERS;
  // Isolation, required by the brief.
  env.LOCALAPPDATA = path.join(ROOT, 'localappdata');
  env.TEMP = path.join(ROOT, 'temp');
  env.TMP = path.join(ROOT, 'temp');
  env.PWTEST_SERVER_REGISTRY = path.join(ROOT, 'registry');
  // NOT in BrowserAI's allowlist. Kept so a held Shift key can never put
  // Firefox's safe-mode dialog on the screen. It changes nothing measured.
  env.MOZ_DISABLE_SAFE_MODE_KEY = '1';
  return env;
}

// ---------------------------------------------------------------- config
// BrowserConfiguration.Generate, key for key.
function makeConfig({ browser, session, har, extraArgs, firefoxPrefs, storageState, ignoreDefaultArgs }) {
  const isFirefox = browser === 'firefox';
  const output = path.join(session, 'output');
  const launchOptions = {};
  if (isFirefox) {
    launchOptions.firefoxUserPrefs = Object.assign({
      'toolkit.winRegisterApplicationRestart': false,
      'signon.rememberSignons': false,
    }, firefoxPrefs || {});
  } else {
    launchOptions.channel = 'chrome-for-testing';
    launchOptions.args = ['--enable-automation', '--disable-blink-features=AutomationControlled', ...(extraArgs || [])];
  }
  launchOptions.headless = true;
  launchOptions.downloadsPath = path.join(session, 'downloads');
  if (ignoreDefaultArgs) launchOptions.ignoreDefaultArgs = ignoreDefaultArgs; // research arm only
  const contextOptions = {
    viewport: { width: 1920, height: 1080 },
    locale: LOCALE,
    timezoneId: TIMEZONE,
    ignoreHTTPSErrors: false,
  };
  if (!isFirefox) contextOptions.permissions = ['clipboard-read'];
  if (har) {
    contextOptions.serviceWorkers = 'block';
    contextOptions.recordHar = { path: har, mode: 'full', content: 'embed' };
  }
  if (storageState) contextOptions.storageState = storageState; // research arm only, never in BrowserAI's config
  return {
    browser: {
      browserName: browser,
      userDataDir: path.join(session, 'profile'),
      launchOptions,
      contextOptions,
    },
    capabilities: ['config', 'vision', 'devtools', 'storage', 'network', 'pdf', 'testing'],
    outputDir: output,
    saveSession: false,
    allowUnrestrictedFileAccess: false,
    console: { level: 'debug' },
    snapshot: { boxes: true },
    codegen: 'none',
    filePaths: 'absolute',
    timeouts: { idle: 3600000 },
    webmcp: true,
  };
}

// ---------------------------------------------------------------- job control
class JobCtl {
  constructor(logFile) {
    this.proc = spawn('pwsh', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', path.join(ROOT, 'rig', 'jobctl.ps1')],
      { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true, env: { ...process.env, TEMP: path.join(ROOT, 'temp'), TMP: path.join(ROOT, 'temp') } });
    this.buf = '';
    this.queue = [];
    this.proc.stdout.setEncoding('utf8');
    this.proc.stderr.setEncoding('utf8');
    this.proc.stderr.on('data', d => fs.appendFileSync(logFile, d));
    this.ready = new Promise(res => { this._ready = res; });
    this.proc.stdout.on('data', d => {
      this.buf += d;
      let i;
      while ((i = this.buf.indexOf('\n')) >= 0) {
        const line = this.buf.slice(0, i).trim();
        this.buf = this.buf.slice(i + 1);
        if (!line) continue;
        let msg; try { msg = JSON.parse(line); } catch { continue; }
        if (msg.ready) { this.pid = msg.pid; this._ready(); continue; }
        const q = this.queue.shift();
        if (q) q(msg);
      }
    });
  }
  cmd(obj) {
    return new Promise(res => { this.queue.push(res); this.proc.stdin.write(JSON.stringify(obj) + '\n'); });
  }
  end() { try { this.proc.stdin.end(); } catch { /* gone */ } }
}

// ---------------------------------------------------------------- MCP child
class Child {
  constructor({ version, configFile, cwd, stderrFile, name }) {
    this.name = name;
    this.spawnedAt = now();
    this.proc = spawn(NODE, [CLI[version], '--config', configFile, '--sandbox'],
      { cwd, env: childEnv(), stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    this.pid = this.proc.pid;
    this.next = 1;
    this.pending = new Map();
    this.buffer = '';
    this.exited = new Promise(res => this.proc.on('exit', (code, signal) => { this.exitAt = now(); this.exitCode = code; res({ code, signal }); }));
    this.proc.stdout.setEncoding('utf8');
    this.proc.stderr.setEncoding('utf8');
    this.proc.stderr.on('data', d => fs.appendFileSync(stderrFile, d));
    this.proc.stdout.on('data', d => {
      this.buffer += d;
      let i;
      while ((i = this.buffer.indexOf('\n')) >= 0) {
        const line = this.buffer.slice(0, i).trim();
        this.buffer = this.buffer.slice(i + 1);
        if (!line) continue;
        let msg; try { msg = JSON.parse(line); } catch { continue; }
        if (msg.id !== undefined && msg.method) {
          // A server-to-client request. Answer it so nothing stalls.
          this.proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: msg.id, result: {} }) + '\n');
          continue;
        }
        if (msg.id !== undefined && this.pending.has(msg.id)) {
          const p = this.pending.get(msg.id);
          this.pending.delete(msg.id);
          p.resolve(msg);
        }
      }
    });
  }
  send(method, params, timeoutMs = 180000) {
    const id = this.next++;
    const t0 = now();
    const p = new Promise((resolve, reject) => {
      this.pending.set(id, { resolve });
      setTimeout(() => { if (this.pending.has(id)) { this.pending.delete(id); reject(new Error(`timeout ${timeoutMs} ms on ${method} ${params && params.name || ''}`)); } }, timeoutMs).unref();
    });
    this.proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
    return p.then(msg => ({ ...msg, ms: now() - t0 }));
  }
  notify(method, params) { this.proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', method, params }) + '\n'); }
  async initialize() {
    const r = await this.send('initialize', { protocolVersion: '2025-11-25', capabilities: {}, clientInfo: { name: 'BrowserAI', version: 'state-across-close-rig' } });
    this.notify('notifications/initialized', {});
    this.initAt = now();
    return r;
  }
  async tool(name, args, timeoutMs) { return this.send('tools/call', { name, arguments: args || {} }, timeoutMs); }
  closeStdin() { this.stdinClosedAt = now(); try { this.proc.stdin.end(); } catch { /* gone */ } }
}

function text(answer) {
  const c = answer && answer.result && answer.result.content;
  if (!Array.isArray(c)) return JSON.stringify(answer && (answer.error || answer.result));
  return c.filter(b => b.type === 'text').map(b => b.text).join('\n');
}
function isError(answer) { return !!(answer && (answer.error || (answer.result && answer.result.isError))); }

// The value a browser_evaluate returned, out of upstream's "### Result" block.
function evalResult(answer) {
  const t = text(answer);
  const m = t.match(/### Result\s*\n([\s\S]*?)(\n### |\n*$)/);
  let raw = m ? m[1].trim() : t;
  raw = raw.replace(/^```(json)?\s*/, '').replace(/```\s*$/, '').trim();
  try { let v = JSON.parse(raw); if (typeof v === 'string') { try { v = JSON.parse(v); } catch { /* plain */ } } return v; } catch { return raw; }
}

function parseTabs(t) {
  const tabs = [];
  for (const line of t.split(/\r?\n/)) {
    const m = line.match(/^- (\d+):( \(current\))? \[(.*)\]\((.*)\)( \[crashed\])?\s*$/);
    if (m) tabs.push({ index: Number(m[1]), current: !!m[2], title: m[3], url: m[4] });
  }
  return tabs;
}

function refsIn(t) {
  const refs = [];
  const re = /- ([a-z]+)(?: "([^"]*)")?[^\n]*?\[ref=([a-z0-9]+)\]/g;
  let m;
  while ((m = re.exec(t))) refs.push({ role: m[1], name: m[2] || '', ref: m[3] });
  return refs;
}

// ---------------------------------------------------------------- the origin
function startPageServer() {
  const pages = {
    '/a': `<!doctype html><meta charset="utf-8"><title>Page A</title><h1>Page A</h1><p>First stop.</p><a href="/b?x=1">to B</a>`,
    '/c': `<!doctype html><meta charset="utf-8"><title>Page C</title><h1>Page C</h1><p id="frag">Fragment target.</p><a href="/a">back to A</a>`,
  };
  const formPage = q => `<!doctype html><meta charset="utf-8"><title>Page B ${q}</title><h1>Page B</h1>
<form id="f" method="post" action="/posted"><label for="name">Name</label> <input id="name" name="name" type="text">
<label for="notes">Notes</label> <textarea id="notes" name="notes"></textarea> <button type="submit">Send</button></form>
<a href="/c">to C</a><div style="height:6000px;background:linear-gradient(#fff,#ccc)">tall</div><p>bottom</p>`;
  const requests = [];
  const server = http.createServer((req, res) => {
    const u = new URL(req.url, 'http://x');
    requests.push({ t: new Date().toISOString(), method: req.method, path: u.pathname + u.search });
    if (u.pathname === '/a') {
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store',
        'set-cookie': ['pc=persistent-cookie; Max-Age=86400; Path=/', 'sc=session-cookie; Path=/'] });
      res.end(pages['/a']); return;
    }
    if (u.pathname === '/b') { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' }); res.end(formPage(u.search)); return; }
    if (u.pathname === '/posted') {
      let body = '';
      req.on('data', d => { body += d; });
      req.on('end', () => {
        res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
        res.end(req.method === 'POST'
          ? `<!doctype html><meta charset="utf-8"><title>Posted</title><h1>Posted</h1><pre id="body">${body.replace(/</g, '&lt;')}</pre>`
          : `<!doctype html><meta charset="utf-8"><title>Not posted (${req.method})</title><h1>This URL was opened with ${req.method}</h1>`);
      });
      return;
    }
    if (pages[u.pathname]) { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' }); res.end(pages[u.pathname]); return; }
    res.writeHead(404); res.end('nope');
  });
  return new Promise(res => server.listen(0, '127.0.0.1', () => res({ server, requests, origin: `http://127.0.0.1:${server.address().port}` })));
}

// ---------------------------------------------------------------- page-side probes
const WRITE = `async () => {
  localStorage.setItem('k', 'local-value');
  sessionStorage.setItem('k', 'session-value');
  document.cookie = 'jsc=js-session-cookie; path=/';
  document.cookie = 'jpc=js-persistent-cookie; path=/; max-age=86400';
  await new Promise((res, rej) => {
    const r = indexedDB.open('rig-db', 1);
    r.onupgradeneeded = () => r.result.createObjectStore('kv');
    r.onsuccess = () => { const db = r.result; const tx = db.transaction('kv', 'readwrite');
      tx.objectStore('kv').put('idb-value', 'k'); tx.oncomplete = () => { db.close(); res(); }; tx.onerror = () => rej(tx.error); };
    r.onerror = () => rej(r.error);
  });
  window.scrollTo(0, 1500);
  await new Promise(r => setTimeout(r, 200));
  return JSON.stringify({ wrote: true, scrollY: window.scrollY });
}`;

const READ = `async () => {
  const ck = n => (document.cookie.match(new RegExp('(?:^|; )' + n + '=([^;]*)')) || [])[1] || null;
  const idb = await new Promise(res => {
    const r = indexedDB.open('rig-db', 1);
    r.onupgradeneeded = () => { r.result.createObjectStore('kv'); };
    r.onsuccess = () => { const db = r.result;
      if (!db.objectStoreNames.contains('kv')) { db.close(); return res(null); }
      const g = db.transaction('kv', 'readonly').objectStore('kv').get('k');
      g.onsuccess = () => { db.close(); res(g.result ?? null); }; g.onerror = () => { db.close(); res(null); }; };
    r.onerror = () => res(null);
  });
  const perm = async n => { try { return (await navigator.permissions.query({ name: n })).state; } catch (e) { return 'n/a: ' + e.name; } };
  const name = document.getElementById('name');
  return JSON.stringify({
    url: location.href, title: document.title,
    cookie_pc: ck('pc'), cookie_sc: ck('sc'), cookie_jsc: ck('jsc'), cookie_jpc: ck('jpc'),
    local: localStorage.getItem('k'), session: sessionStorage.getItem('k'), idb,
    formName: name ? name.value : '(no #name on this page)',
    scrollY: window.scrollY, historyLength: history.length,
    perm_geolocation: await perm('geolocation'), perm_clipboard_read: await perm('clipboard-read'),
  });
}`;

const COOKIES_CODE = `async (page) => JSON.stringify((await page.context().cookies()).map(c => ({ name: c.name, value: c.value, expires: c.expires, session: c.expires === -1 })))`;
// The vm context browser_run_code_unsafe runs in holds `page` and nothing else -- no URL, no setTimeout.
const GRANT_CODE = `async (page) => { const origin = page.url().split('/').slice(0, 3).join('/'); await page.context().grantPermissions(['geolocation'], { origin }); return 'granted geolocation to ' + origin; }`;

// ---------------------------------------------------------------- process accounting
function summarise(snap, childPid) {
  const procs = (snap && snap.procs) || [];
  const browserRoot = BROWSERS.toLowerCase();
  let nodeWs = 0, nodePriv = 0, brWs = 0, brPriv = 0, brCount = 0, other = 0;
  for (const p of procs) {
    const exe = (p.exe || '').toLowerCase();
    if (p.pid === childPid) { nodeWs += p.ws; nodePriv += p.priv; }
    else if (exe.startsWith(browserRoot)) { brWs += p.ws; brPriv += p.priv; brCount++; }
    else other++;
  }
  const mb = b => Math.round(b / 1048576 * 10) / 10;
  return { active: snap && snap.active, total: snap && snap.total, browserProcs: brCount, otherProcs: other,
    nodeWsMB: mb(nodeWs), nodePrivMB: mb(nodePriv), browserWsMB: mb(brWs), browserPrivMB: mb(brPriv),
    totalWsMB: mb(nodeWs + brWs), totalPrivMB: mb(nodePriv + brPriv), peakJobCommitMB: mb((snap && snap.peakJobCommit) || 0) };
}

async function waitJobEmpty(job, maxMs) {
  const t0 = now();
  for (;;) {
    const s = await job.cmd({ op: 'snap' });
    if (!s.ok || s.active === 0) return { ms: now() - t0, snap: s };
    if (now() - t0 > maxMs) return { ms: now() - t0, snap: s, timedOut: true };
    await wait(25);
  }
}

function fileInfo(p) {
  try {
    const st = fs.statSync(p);
    const buf = fs.readFileSync(p);
    return { exists: true, size: st.size, mtime: st.mtime.toISOString(), sha256: crypto.createHash('sha256').update(buf).digest('hex').slice(0, 16) };
  } catch { return { exists: false }; }
}

function listTree(dir, base = dir, out = []) {
  let ents = [];
  try { ents = fs.readdirSync(dir, { withFileTypes: true }); } catch { return out; }
  for (const e of ents) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) listTree(full, base, out);
    else { try { const st = fs.statSync(full); out.push({ path: path.relative(base, full), size: st.size, mtime: st.mtime.toISOString() }); } catch { /* raced */ } }
  }
  return out;
}

// A run: a directory, a report written as it goes, a job controller.
class Run {
  constructor(name) {
    this.name = name;
    this.dir = path.join(ROOT, 'runs', name);
    fs.mkdirSync(this.dir, { recursive: true });
    this.session = path.join(this.dir, 'session');
    for (const d of ['profile', 'output', 'downloads']) fs.mkdirSync(path.join(this.session, d), { recursive: true });
    this.report = { name, utcStart: new Date().toISOString(), steps: [] };
    this.job = new JobCtl(path.join(this.dir, 'jobctl.stderr.txt'));
    this.childCount = 0;
  }
  save() { fs.writeFileSync(path.join(this.dir, 'report.json'), JSON.stringify(this.report, null, 2)); }
  note(k, v) { this.report[k] = v; this.save(); }
  step(label, data) { this.report.steps.push({ label, at: new Date().toISOString(), ...data }); this.save(); }
  async spawnChild(version, config) {
    const n = ++this.childCount;
    const configFile = path.join(this.dir, `config-${n}.json`);
    fs.writeFileSync(configFile, JSON.stringify(config, null, 2));
    const created = await this.job.cmd({ op: 'create' });
    if (!created.ok) throw new Error('job create: ' + JSON.stringify(created));
    const child = new Child({ version, configFile, cwd: this.session, stderrFile: path.join(this.dir, `child-${n}.stderr.txt`), name: `child-${n}` });
    const assigned = await this.job.cmd({ op: 'assign', pid: child.pid });
    if (!assigned.ok) throw new Error('job assign: ' + JSON.stringify(assigned));
    child.creation = assigned.creation;
    const init = await child.initialize();
    const snap = await this.job.cmd({ op: 'snap' });
    this.step(`child-${n} started`, { pid: child.pid, creationFileTime: assigned.creation, initMs: init.ms, spawnToInitMs: child.initAt - child.spawnedAt,
      serverInfo: init.result && init.result.serverInfo, job: summarise(snap, child.pid) });
    return child;
  }
  async snap(label, child) {
    const s = await this.job.cmd({ op: 'snap' });
    const sum = summarise(s, child && child.pid);
    this.step(label, { job: sum, procs: (s.procs || []).map(p => ({ pid: p.pid, ppid: p.ppid, exe: p.exe && path.basename(p.exe), wsMB: Math.round(p.ws / 104857.6) / 10 })) });
    return sum;
  }
  async call(child, label, name, args, extra) {
    const r = await child.tool(name, args);
    const t = text(r);
    this.step(label, { tool: name, args, ms: Math.round(r.ms * 10) / 10, isError: isError(r), text: t.length > 6000 ? t.slice(0, 6000) + '...[cut]' : t, ...(extra || {}) });
    return { r, t, ms: r.ms };
  }
  // Graceful teardown: close stdin, time the exit and the job emptying.
  async teardown(child, label) {
    child.closeStdin();
    const t0 = child.stdinClosedAt;
    const exited = await Promise.race([child.exited, wait(30000).then(() => null)]);
    const exitMs = exited ? child.exitAt - t0 : null;
    const empty = await waitJobEmpty(this.job, 30000);
    const closed = await this.job.cmd({ op: 'close' });
    this.step(label, { stdinCloseToExitMs: exitMs && Math.round(exitMs), exitCode: child.exitCode, stdinCloseToJobEmptyMs: Math.round(now() - t0),
      jobEmptyWaitMs: Math.round(empty.ms), jobEmptyTimedOut: !!empty.timedOut, jobClose: closed });
    return { exitMs, toEmptyMs: now() - t0 };
  }
  async kill(child, label) {
    const closed = await this.job.cmd({ op: 'close' });
    await Promise.race([child.exited, wait(10000)]);
    this.step(label, { jobClose: closed, exitCode: child.exitCode });
  }
  async finish() {
    try { await this.job.cmd({ op: 'close' }); } catch { /* already */ }
    this.job.end();
    this.report.utcEnd = new Date().toISOString();
    this.save();
  }
}

module.exports = { ROOT, REPO, NODE, BROWSERS, CLI, wait, now, makeConfig, Run, text, isError, evalResult, parseTabs, refsIn,
  startPageServer, WRITE, READ, COOKIES_CODE, GRANT_CODE, summarise, fileInfo, listTree, waitJobEmpty };
