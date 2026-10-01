// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Track C probe 2: the dashboard's effects measured through the SAME path BrowserAI forwards --
// a real @playwright/mcp 0.0.82 child (the payload's own cli.js) driven over stdio JSON-RPC --
// plus reload LIVENESS (not only first listing) of the real dashboard UI.
//
// Safety, as probe 1: PWTEST_SERVER_REGISTRY and PWTEST_SOCKETS_DIR must be under .work/zoomout/c;
// TEMP/TMP point into scratch; every browser is headless (the MCP config says so explicitly,
// because upstream's default on Windows is HEADED); browserName is set so upstream does not
// default to the machine's own Google Chrome; the dashboard runs with --port and `reveal` is
// never called; the real registry is listed by name before and after and never written.
'use strict';
const fs = require('fs');
const path = require('path');
const http = require('http');
const { spawn } = require('child_process');

const REPO = 'C:/Source/SixFive7/BrowserAI';
const C = path.join(REPO, '.work', 'zoomout', 'c');
const PWLIB = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const MCPCLI = path.join(REPO, 'payload', 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js');
const REG = process.env.PWTEST_SERVER_REGISTRY;
const SOCK = process.env.PWTEST_SOCKETS_DIR;
const REAL = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'b');
const OUT = path.join(C, 'evidence');
const LOG = path.join(OUT, 'probe2.log');
const RESULTS = path.join(OUT, 'probe2-results.json');
const under = (p) => p && path.resolve(p).toLowerCase().startsWith(path.resolve(C).toLowerCase() + path.sep);
if (!under(REG) || !under(SOCK)) { console.error('REFUSING: registry/sockets not under ' + C); process.exit(2); }
const TMP = path.join(C, 'tmp');
fs.mkdirSync(TMP, { recursive: true });
process.env.TEMP = TMP; process.env.TMP = TMP;
fs.writeFileSync(LOG, '');
const t0 = Date.now();
const log = (...a) => { const line = `[+${String(Date.now() - t0).padStart(6)} ms] ` + a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '); fs.appendFileSync(LOG, line + '\n'); console.log(line); };
const results = { versions: {}, experiments: {} };
const record = (k, v) => { results.experiments[k] = v; fs.writeFileSync(RESULTS, JSON.stringify(results, null, 2)); };
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const withTimeout = (p, ms, label) => Promise.race([
  p.then(v => ({ ok: true, value: v }), e => ({ ok: false, error: String(e && e.message || e) })),
  sleep(ms).then(() => ({ ok: false, timedOut: true, label, ms })),
]);
const pw = require(PWLIB);
const { ws: WebSocket } = require(path.join(PWLIB, 'lib', 'utilsBundle'));
results.versions.playwrightCore = require(path.join(PWLIB, 'package.json')).version;
results.versions.playwrightMcp = require(path.join(REPO, 'payload', 'mcp', 'node_modules', '@playwright', 'mcp', 'package.json')).version;
results.versions.node = process.version;

const toStop = [];
async function cleanup() { for (const f of toStop.reverse()) { try { await withTimeout(Promise.resolve().then(f), 15000, 'cleanup'); } catch {} } toStop.length = 0; }
const watchdog = setTimeout(async () => { log('WATCHDOG: 8 minutes, stopping'); await cleanup(); process.exit(3); }, 8 * 60 * 1000);
const realListing = () => { try { return fs.readdirSync(REAL).sort(); } catch (e) { return ['<unreadable ' + e.message + '>']; } };

const hits = {};
const pageServer = http.createServer((req, res) => {
  const u = new URL(req.url, 'http://x');
  hits[u.pathname] = (hits[u.pathname] || 0) + 1;
  if (u.pathname === '/favicon.ico') { res.writeHead(404); return res.end(); }
  res.writeHead(200, { 'content-type': 'text/html' });
  res.end(`<title>${u.pathname}</title><h1>${u.pathname}</h1><input id="q">`);
});

// ---- an MCP stdio client, newline-delimited JSON-RPC, speaking to the payload's own cli.js ----
class McpChild {
  constructor(configPath, cwd) {
    this.proc = spawn(process.execPath, [MCPCLI, '--config', configPath], { cwd, env: { ...process.env }, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    this.nextId = 1; this.pending = new Map(); this.buf = ''; this.stderr = '';
    this.proc.stdout.on('data', d => {
      this.buf += String(d);
      let i;
      while ((i = this.buf.indexOf('\n')) >= 0) {
        const line = this.buf.slice(0, i); this.buf = this.buf.slice(i + 1);
        if (!line.trim()) continue;
        let msg; try { msg = JSON.parse(line); } catch { continue; }
        if (msg.id !== undefined && this.pending.has(msg.id)) { const r = this.pending.get(msg.id); this.pending.delete(msg.id); r(msg); }
      }
    });
    this.proc.stderr.on('data', d => { this.stderr += String(d); });
  }
  send(method, params) { const id = this.nextId++; return new Promise(r => { this.pending.set(id, r); this.proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n'); }); }
  notify(method, params) { this.proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', method, params }) + '\n'); }
  call(name, args) { return this.send('tools/call', { name, arguments: args || {} }); }
  text(resp) { try { return resp.result.content.filter(c => c.type === 'text').map(c => c.text).join('\n'); } catch { return JSON.stringify(resp); } }
  async stop() { try { this.proc.stdin.end(); } catch {} await sleep(1500); try { this.proc.kill(); } catch {} }
}

class DashClient {
  constructor(url) { this.url = url; this.nextId = 1; this.pending = new Map(); this.events = []; }
  open() { return new Promise((resolve, reject) => { this.sock = new WebSocket(this.url); this.sock.on('open', resolve); this.sock.on('error', reject);
    this.sock.on('message', (m) => { const msg = JSON.parse(String(m)); if (msg.id !== undefined && this.pending.has(msg.id)) { const r = this.pending.get(msg.id); this.pending.delete(msg.id); r(msg); return; } this.events.push({ at: Date.now() - t0, method: msg.method, params: msg.params }); }); }); }
  call(method, params) { const id = this.nextId++; return new Promise(r => { this.pending.set(id, r); this.sock.send(JSON.stringify({ id, method, params })); }); }
  async waitFor(pred, ms) { const s = Date.now(); while (Date.now() - s < ms) { const f = this.events.find(pred); if (f) return f; await sleep(50); } return null; }
  close() { return new Promise(r => { if (!this.sock || this.sock.readyState === 3) return r(); this.sock.once('close', () => r()); this.sock.close(); setTimeout(r, 3000); }); }
}

(async () => {
  const realBefore = realListing();
  fs.writeFileSync(path.join(OUT, 'real-registry-before-probe2.txt'), realBefore.join('\n') + '\n');
  fs.rmSync(REG, { recursive: true, force: true }); fs.mkdirSync(REG, { recursive: true }); fs.mkdirSync(SOCK, { recursive: true });
  log('E20 real registry before:', realBefore.length);
  await new Promise(r => pageServer.listen(0, '127.0.0.1', r));
  toStop.push(() => new Promise(r => pageServer.close(() => r())));
  const base = `http://127.0.0.1:${pageServer.address().port}`;

  // ---------- E20: a real @playwright/mcp child, configured headless on Playwright's own chromium ----------
  const mcpDir = path.join(C, 'mcp'); fs.rmSync(mcpDir, { recursive: true, force: true }); fs.mkdirSync(path.join(mcpDir, 'out'), { recursive: true });
  const configPath = path.join(mcpDir, 'config.json');
  fs.writeFileSync(configPath, JSON.stringify({
    browser: { browserName: 'chromium', userDataDir: path.join(mcpDir, 'profile'), launchOptions: { headless: true }, contextOptions: { viewport: { width: 1000, height: 700 } } },
    capabilities: ['devtools'], outputDir: path.join(mcpDir, 'out'), console: { level: 'debug' }, codegen: 'none', filePaths: 'absolute',
  }, null, 2));
  const mcp = new McpChild(configPath, path.join(mcpDir, 'out'));
  toStop.push(() => mcp.stop());
  const init = await withTimeout(mcp.send('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'zoomout-c-mcp', version: '0' } }), 20000, 'initialize');
  mcp.notify('notifications/initialized', {});
  log('E20 initialize ->', init.ok ? init.value.result.serverInfo : init);
  const nav1 = await withTimeout(mcp.call('browser_navigate', { url: base + '/noicon' }), 60000, 'navigate 1');
  log('E20 browser_navigate ->', nav1.ok ? mcp.text(nav1.value).split('\n').slice(0, 6).join(' | ') : nav1);
  const scratch = fs.readdirSync(REG); const desc = scratch.length ? JSON.parse(fs.readFileSync(path.join(REG, scratch[0]), 'utf-8')) : null;
  log('E20 scratch registry now', scratch, '; descriptor title', desc && desc.title, 'workspaceDir', desc && desc.workspaceDir, '; real count', realListing().length);
  record('E20', { serverInfo: init.ok ? init.value.result.serverInfo : null, scratch, title: desc && desc.title, workspaceDir: desc && desc.workspaceDir, realCount: realListing().length });

  // ---------- E21: the dashboard lists it, and the session's console hears about it ----------
  const entry = path.join(PWLIB, 'lib', 'entry', 'dashboardApp.js');
  const dash = spawn(process.execPath, [entry, '--port=0', '--host=127.0.0.1'], { env: { ...process.env }, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  toStop.push(async () => { try { dash.stdin.end(); } catch {} await sleep(500); try { dash.kill(); } catch {} });
  let url = null; dash.stdout.on('data', d => { const m = String(d).match(/Listening on (\S+)/); if (m) url = m[1]; });
  const ts = Date.now(); while (!url && Date.now() - ts < 20000) await sleep(50);
  const port = Number(new URL(url).port);
  const loc = await new Promise(r => http.get({ host: '127.0.0.1', port, path: '/' }, res => { r(res.headers.location); res.resume(); }));
  const wsUrl = `ws://127.0.0.1:${port}/${new URL(loc, 'http://x').searchParams.get('ws')}`;
  const consoleBefore = await withTimeout(mcp.call('browser_console_messages', { level: 'error', all: true }), 20000, 'console before');
  const A = new DashClient(wsUrl); await A.open();
  const tabsEv = await A.waitFor(e => e.method === 'tabs' && e.params.tabs.some(t => t.url.startsWith(base)), 20000);
  await sleep(2500);
  const consoleAfter = await withTimeout(mcp.call('browser_console_messages', { level: 'error', all: true }), 20000, 'console after');
  const cb = consoleBefore.ok ? mcp.text(consoleBefore.value) : JSON.stringify(consoleBefore);
  const ca = consoleAfter.ok ? mcp.text(consoleAfter.value) : JSON.stringify(consoleAfter);
  const count404 = (s) => (s.match(/404/g) || []).length;
  log('E21 dashboard', url, '; MCP console errors before listing:', count404(cb), '404s; after listing:', count404(ca), '404s; /favicon.ico hits', hits['/favicon.ico'] || 0);
  fs.writeFileSync(path.join(OUT, 'probe2-console-after-listing.txt'), ca);
  record('E21', { dashboardUrl: url, console404sBefore: count404(cb), console404sAfter: count404(ca), faviconHits: hits['/favicon.ico'] || 0, consoleAfterExcerpt: ca.slice(0, 600) });

  // ---------- E22: pause from the dashboard, the dashboard goes away, an MCP call follows ----------
  const t = tabsEv.params.tabs.find(x => x.url.startsWith(base));
  await A.call('selectTab', { browser: t.browser, context: t.context, page: t.page });
  await A.waitFor(e => e.method === 'frame', 5000);
  const pr = await A.call('debuggerPause', {});
  await A.close();
  log('E22 debuggerPause ->', pr, '; dashboard connection closed without resuming');
  await sleep(1000);
  const tNav = Date.now();
  let navDoneAt = null; let navResp = null;
  const navP = mcp.call('browser_navigate', { url: base + '/after-pause' }).then(r => { navDoneAt = Date.now(); navResp = r; });
  await sleep(15000);
  log('E22 MCP browser_navigate after the dashboard left: answered within 15 s?', navDoneAt !== null, navDoneAt ? (navDoneAt - tNav) + ' ms' : '');
  // A concurrent snapshot while the navigate is parked: is the child otherwise alive, and does it say "Paused"?
  const snap = await withTimeout(mcp.call('browser_snapshot', {}), 15000, 'snapshot while parked');
  const snapText = snap.ok ? mcp.text(snap.value) : JSON.stringify(snap);
  log('E22 concurrent browser_snapshot ->', snap.ok ? 'answered' : snap, '; mentions Paused:', /### Paused/.test(snapText), '; excerpt:', snapText.replace(/\n/g, ' | ').slice(0, 400));
  const tRes = Date.now();
  const res = await withTimeout(mcp.call('browser_resume', {}), 20000, 'resume');
  await sleep(2000);
  log('E22 concurrent browser_resume ->', res.ok ? mcp.text(res.value).replace(/\n/g, ' | ').slice(0, 300) : res, 'after', Date.now() - tRes, 'ms; the parked navigate answered:', navDoneAt !== null, navDoneAt ? ('at +' + (navDoneAt - tNav) + ' ms from its call') : '');
  await withTimeout(navP, 10000, 'navigate settle');
  record('E22', { pauseResp: pr, navigateAnsweredWithin15s: navDoneAt !== null && navDoneAt - tNav < 15000, navigateAnsweredAtMs: navDoneAt ? navDoneAt - tNav : null,
    snapshotWhileParked: snap.ok ? 'answered' : snap, snapshotSaysPaused: /### Paused/.test(snapText), resume: res.ok ? mcp.text(res.value).slice(0, 300) : res,
    navigateText: navResp ? mcp.text(navResp).slice(0, 300) : null });
  fs.writeFileSync(path.join(OUT, 'probe2-snapshot-while-parked.txt'), snapText);

  // ---------- E23: the dashboard closes the session's browser; what does the next MCP call see? ----------
  const F = new DashClient(wsUrl); await F.open();
  const s2 = await F.waitFor(e => e.method === 'sessions' && e.params.sessions.some(s => s.title === 'zoomout-c-mcp'), 20000);
  const guid = s2.params.sessions.find(s => s.title === 'zoomout-c-mcp').browser.guid;
  const tabsBefore = await withTimeout(mcp.call('browser_tabs', { action: 'list' }), 20000, 'tabs before');
  const cs = await withTimeout(F.call('closeSession', { browser: guid }), 15000, 'closeSession');
  await sleep(1500);
  const after1 = await withTimeout(mcp.call('browser_tabs', { action: 'list' }), 30000, 'tabs after 1');
  const after2 = await withTimeout(mcp.call('browser_tabs', { action: 'list' }), 30000, 'tabs after 2');
  const regAfter = fs.readdirSync(REG);
  const tx = (r) => r.ok ? mcp.text(r.value).replace(/\n/g, ' | ').slice(0, 300) : JSON.stringify(r);
  log('E23 MCP tabs before close:', tx(tabsBefore));
  log('E23 dashboard closeSession ->', cs);
  log('E23 first MCP call after:', tx(after1));
  log('E23 second MCP call after:', tx(after2));
  log('E23 scratch registry now', regAfter);
  record('E23', { tabsBefore: tx(tabsBefore), closeSession: cs, firstCallAfter: tx(after1), secondCallAfter: tx(after2), scratchAfter: regAfter });
  await F.close();

  // ---------- E24: reload LIVENESS: after each reload, does the tab still hear a change? ----------
  const ctxL = await pw.chromium.launchPersistentContext(path.join(C, 'profile-live'), { headless: true, handleSIGINT: false, handleSIGTERM: false });
  toStop.push(() => ctxL.close().catch(() => {}));
  await ctxL.browser().bind('zoomout-c-live-v0', { workspaceDir: C });
  const liveGuid = ctxL.browser()._guid;
  const descPath = path.join(REG, liveGuid);
  const viewer = await pw.chromium.launch({ headless: true });
  toStop.push(() => viewer.close().catch(() => {}));
  const vp = await (await viewer.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
  const bodyHas = async (s) => (await vp.evaluate(() => document.body.innerText).catch(() => '')).includes(s);
  const waitText = async (s, ms) => { const st = Date.now(); while (Date.now() - st < ms) { if (await bodyHas(s)) return Date.now() - st; await sleep(100); } return null; };
  await vp.goto(url);
  log('E24 first load shows v0 after', await waitText('zoomout-c-live-v0', 15000), 'ms');
  const trials = [];
  for (let i = 1; i <= 10; i++) {
    await vp.reload();
    const listed = await waitText('zoomout-c-live-v' + (i - 1), 8000);
    const d = JSON.parse(fs.readFileSync(descPath, 'utf-8')); d.title = 'zoomout-c-live-v' + i; fs.writeFileSync(descPath, JSON.stringify(d, null, 2));
    const heard = await waitText('zoomout-c-live-v' + i, 6000);
    trials.push({ i, listedMs: listed, heardChangeMs: heard });
    log('E24 reload', i, '-> listed', listed, 'ms; heard the title change', heard, 'ms');
  }
  record('E24', { trials, listed: trials.filter(x => x.listedMs !== null).length, live: trials.filter(x => x.heardChangeMs !== null).length, of: trials.length });

  await cleanup();
  await sleep(1500);
  const realAfter = realListing();
  fs.writeFileSync(path.join(OUT, 'real-registry-after-probe2.txt'), realAfter.join('\n') + '\n');
  const missing = realBefore.filter(n => !realAfter.includes(n));
  log('E25 real registry after:', realAfter.length, '; missing from before:', missing.length, missing);
  log('E25 mcp child exit', mcp.proc.exitCode, mcp.proc.killed, '; dashboard exit', dash.exitCode, dash.killed);
  fs.writeFileSync(path.join(OUT, 'probe2-mcp-stderr.txt'), mcp.stderr);
  record('E25', { realBefore: realBefore.length, realAfter: realAfter.length, missing });
  clearTimeout(watchdog);
  process.exit(0);
})().catch(async (e) => { log('FATAL', String(e && e.stack || e)); await cleanup(); fs.writeFileSync(path.join(OUT, 'real-registry-after-probe2.txt'), realListing().join('\n') + '\n'); process.exit(1); });
