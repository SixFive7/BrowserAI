// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Track C probe: what Playwright's dashboard (playwright-core 1.64.0-alpha-1789764292000,
// the payload BrowserAI ships) does to a browser it lists, measured ONLY against browsers
// this script launches, with the registry and the singleton pipe redirected into .work.
//
// Safety:
//  * PWTEST_SERVER_REGISTRY and PWTEST_SOCKETS_DIR must point under .work/zoomout/c, or it refuses.
//  * TEMP/TMP point under .work/zoomout/c/tmp, so the dashboard's per-connection recording dir
//    and Playwright's artifacts dir land in scratch.
//  * Every browser is headless. The dashboard runs with --port, so it never opens its app window.
//  * The dashboard's `reveal` method (explorer /select,) is NEVER called: it would open a window.
//  * The real %LOCALAPPDATA%\ms-playwright\b is listed (names only) before and after, never written.
//  * Every process this starts is stopped through its own handle before exit.
'use strict';
const fs = require('fs');
const path = require('path');
const http = require('http');
const { spawn } = require('child_process');

const REPO = 'C:/Source/SixFive7/BrowserAI';
const C = path.join(REPO, '.work', 'zoomout', 'c');
const PWLIB = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const REG = process.env.PWTEST_SERVER_REGISTRY;
const SOCK = process.env.PWTEST_SOCKETS_DIR;
const REAL = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'b');
const OUT = path.join(C, 'evidence');
const LOG = path.join(OUT, 'probe.log');
const RESULTS = path.join(OUT, 'probe-results.json');

const under = (p) => p && path.resolve(p).toLowerCase().startsWith(path.resolve(C).toLowerCase() + path.sep);
if (!under(REG) || !under(SOCK)) {
  console.error('REFUSING: PWTEST_SERVER_REGISTRY and PWTEST_SOCKETS_DIR must be under ' + C);
  process.exit(2);
}
const TMP = path.join(C, 'tmp');
fs.mkdirSync(TMP, { recursive: true });
process.env.TEMP = TMP;
process.env.TMP = TMP;

fs.mkdirSync(OUT, { recursive: true });
fs.writeFileSync(LOG, '');
const t0 = Date.now();
const log = (...a) => {
  const line = `[+${String(Date.now() - t0).padStart(6)} ms] ` + a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' ');
  fs.appendFileSync(LOG, line + '\n');
  console.log(line);
};
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
results.versions.node = process.version;

const toStop = [];
let dashboardChild = null;
const watchdog = setTimeout(async () => {
  log('WATCHDOG: 8 minutes elapsed, stopping everything');
  await cleanup();
  process.exit(3);
}, 8 * 60 * 1000);

async function cleanup() {
  for (const f of toStop.reverse()) {
    try { await withTimeout(Promise.resolve().then(f), 15000, 'cleanup'); } catch {}
  }
  toStop.length = 0;
}

// ---- a tiny page server, so the dashboard's side effects on a page are countable ----
const hits = {};
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');
const pageServer = http.createServer((req, res) => {
  const u = new URL(req.url, 'http://x');
  hits[u.pathname] = (hits[u.pathname] || 0) + 1;
  if (u.pathname === '/icon.png') { res.writeHead(200, { 'content-type': 'image/png' }); return res.end(PNG); }
  if (u.pathname === '/favicon.ico') { res.writeHead(404); return res.end(); }
  if (u.pathname === '/other') { res.writeHead(200, { 'content-type': 'text/html' }); return res.end('<title>navigated by the dashboard</title><h1>other</h1>'); }
  res.writeHead(200, { 'content-type': 'text/html' });
  res.end('<title>owner page</title><link rel="icon" href="/icon.png"><h1>owner</h1><input id="q" autofocus>');
});

function realListing() {
  try { return fs.readdirSync(REAL).sort(); } catch (e) { return ['<unreadable: ' + e.message + '>']; }
}

// A raw client of the dashboard's websocket, speaking the same {id, method, params} frames the UI does.
class DashClient {
  constructor(url, headers) {
    this.url = url; this.headers = headers; this.nextId = 1; this.pending = new Map();
    this.events = []; this.listeners = [];
  }
  open() {
    return new Promise((resolve, reject) => {
      this.sock = new WebSocket(this.url, { headers: this.headers });
      this.sock.on('open', () => resolve());
      this.sock.on('error', (e) => reject(e));
      this.sock.on('unexpected-response', (req, res) => reject(new Error('unexpected-response ' + res.statusCode)));
      this.sock.on('message', (m) => {
        const msg = JSON.parse(String(m));
        if (msg.id !== undefined && this.pending.has(msg.id)) {
          const { resolve: r } = this.pending.get(msg.id); this.pending.delete(msg.id); r(msg);
          return;
        }
        this.events.push({ at: Date.now() - t0, method: msg.method, params: msg.params });
        for (const l of this.listeners) l(msg);
      });
    });
  }
  call(method, params) {
    const id = this.nextId++;
    return new Promise((resolve) => {
      this.pending.set(id, { resolve });
      this.sock.send(JSON.stringify({ id, method, params }));
    });
  }
  waitFor(pred, ms) {
    return new Promise((resolve) => {
      const found = this.events.find(e => pred(e));
      if (found) return resolve(found);
      const l = (msg) => { if (pred({ method: msg.method, params: msg.params })) { this.listeners = this.listeners.filter(x => x !== l); resolve({ method: msg.method, params: msg.params }); } };
      this.listeners.push(l);
      setTimeout(() => { this.listeners = this.listeners.filter(x => x !== l); resolve(null); }, ms);
    });
  }
  count(method, sinceAt) { return this.events.filter(e => e.method === method && (sinceAt === undefined || e.at >= sinceAt)).length; }
  close() { return new Promise(r => { if (!this.sock || this.sock.readyState === 3) return r(); this.sock.once('close', () => r()); this.sock.close(); setTimeout(r, 3000); }); }
}

function httpGet(port, pathName, headers = {}, method = 'GET') {
  return new Promise((resolve) => {
    const req = http.request({ host: '127.0.0.1', port, path: pathName, method, headers }, (res) => {
      const chunks = [];
      res.on('data', c => chunks.push(c));
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, bytes: Buffer.concat(chunks).length }));
    });
    req.on('error', (e) => resolve({ error: e.message }));
    req.end();
  });
}

(async () => {
  // ---------- E0: preconditions ----------
  const realBefore = realListing();
  fs.writeFileSync(path.join(OUT, 'real-registry-before-probe.txt'), realBefore.join('\n') + '\n');
  log('E0 real registry (names only) before:', realBefore.length);
  fs.rmSync(REG, { recursive: true, force: true });
  fs.mkdirSync(REG, { recursive: true });
  fs.mkdirSync(SOCK, { recursive: true });
  log('E0 scratch registry:', REG, 'sockets dir:', SOCK, 'TEMP:', process.env.TEMP);
  record('E0', { realBefore: realBefore.length, scratchRegistry: REG, socketsDir: SOCK });

  await new Promise(r => pageServer.listen(0, '127.0.0.1', r));
  const pagePort = pageServer.address().port;
  toStop.push(() => new Promise(r => pageServer.close(() => r())));
  const PAGE = `http://127.0.0.1:${pagePort}/page`;
  log('E0 page server', PAGE);

  // ---------- E1: an owner browser, bound the way @playwright/mcp binds every browser ----------
  const ownerProfile = path.join(C, 'profile-owner');
  fs.rmSync(ownerProfile, { recursive: true, force: true });
  const ctx = await pw.chromium.launchPersistentContext(ownerProfile, { headless: true, handleSIGINT: false, handleSIGTERM: false, viewport: { width: 1000, height: 700 } });
  toStop.push(() => ctx.close().catch(() => {}));
  const browser = ctx.browser();
  const bound = await browser.bind('zoomout-c-owner', { workspaceDir: C });
  const page = ctx.pages()[0] || await ctx.newPage();
  await page.goto(PAGE);
  const ownerGuid = browser._guid;
  const scratchFiles = fs.readdirSync(REG);
  const realAfterBind = realListing();
  const descriptor = JSON.parse(fs.readFileSync(path.join(REG, ownerGuid), 'utf-8'));
  log('E1 owner bound guid=' + ownerGuid, 'endpoint=' + bound.endpoint);
  log('E1 scratch registry holds', scratchFiles, 'real registry count', realAfterBind.length, '(was', realBefore.length + ')');
  log('E1 descriptor keys', Object.keys(descriptor), 'browser keys', Object.keys(descriptor.browser), 'launchOptions keys', Object.keys(descriptor.browser.launchOptions || {}));
  fs.writeFileSync(path.join(OUT, 'owner-descriptor.json'), JSON.stringify(descriptor, null, 2));
  record('E1', { ownerGuid, endpoint: bound.endpoint, scratchFiles, realCountAfterBind: realAfterBind.length, descriptorKeys: Object.keys(descriptor), launchOptionKeys: Object.keys(descriptor.browser.launchOptions || {}) });

  // ---------- E2: dead descriptors, planted the way T7's rig plants them ----------
  const dead = [];
  for (let i = 0; i < 5; i++) {
    const guid = 'browser@' + ('deadbeef'.repeat(4)).slice(0, 30) + String(i).padStart(2, '0');
    const d = { playwrightVersion: descriptor.playwrightVersion, playwrightLib: descriptor.playwrightLib, title: 'zoomout-c-dead-' + i,
      browser: { guid, browserName: 'chromium', launchOptions: {} }, endpoint: '\\\\.\\pipe\\pw-zoomout-c-nobody-' + i, workspaceDir: C };
    fs.writeFileSync(path.join(REG, guid), JSON.stringify(d, null, 2));
    dead.push(guid);
  }
  log('E2 planted dead descriptors', dead.length, 'scratch now', fs.readdirSync(REG).length);
  record('E2', { planted: dead.length, scratchCount: fs.readdirSync(REG).length });

  // ---------- E3: the dashboard, exactly as the probe rig and `playwright-cli show --port` start it ----------
  const entry = path.join(PWLIB, 'lib', 'entry', 'dashboardApp.js');
  const hitsBeforeDashboard = { ...hits };
  dashboardChild = spawn(process.execPath, [entry, '--port=0', '--host=127.0.0.1', '--workspaceDir=' + C], {
    env: { ...process.env }, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true,
  });
  toStop.push(async () => { try { dashboardChild.stdin.end(); } catch {} await sleep(500); try { dashboardChild.kill(); } catch {} });
  log('E3 dashboard pid', dashboardChild.pid);
  let url = null; let stdoutAll = ''; let stderrAll = '';
  dashboardChild.stdout.on('data', d => { stdoutAll += String(d); const m = String(d).match(/Listening on (\S+)/); if (m) url = m[1]; });
  dashboardChild.stderr.on('data', d => { stderrAll += String(d); });
  dashboardChild.on('exit', (code, sig) => log('dashboard exited code=' + code + ' sig=' + sig));
  const tStart = Date.now();
  while (!url && Date.now() - tStart < 20000) await sleep(50);
  log('E3 dashboard url', url, 'after', Date.now() - tStart, 'ms; stdout:', stdoutAll.trim(), 'stderr:', stderrAll.trim());
  if (!url) throw new Error('dashboard did not print a URL');
  const port = Number(new URL(url).port);

  const rootResp = await httpGet(port, '/');
  const badHost = await httpGet(port, '/', { host: 'evil.example:' + port });
  const rebinding = await httpGet(port, '/index.html', { host: 'attacker.localtest.me:' + port });
  const indexResp = await httpGet(port, '/index.html');
  const traversal = await httpGet(port, '/..%2F..%2F..%2Fpackage.json');
  const traversal2 = await httpGet(port, '/assets/../../../package.json');
  const options = await httpGet(port, '/', { origin: 'https://evil.example' }, 'OPTIONS');
  const wsGuid = rootResp.headers && rootResp.headers.location ? new URL(rootResp.headers.location, 'http://x').searchParams.get('ws') : null;
  log('E3 GET / ->', rootResp.status, rootResp.headers && rootResp.headers.location);
  log('E3 GET / with Host evil.example ->', badHost.status, '; Host attacker.localtest.me ->', rebinding.status);
  log('E3 GET /index.html ->', indexResp.status, 'headers', indexResp.headers);
  log('E3 traversal ->', traversal.status, traversal2.status, '; OPTIONS with evil Origin ->', options.status, options.headers);
  record('E3', { url, pid: dashboardChild.pid, startMs: Date.now() - tStart, rootStatus: rootResp.status, location: rootResp.headers && rootResp.headers.location,
    badHostStatus: badHost.status, rebindHostStatus: rebinding.status, indexStatus: indexResp.status, indexHeaders: indexResp.headers,
    traversalStatus: [traversal.status, traversal2.status], optionsStatus: options.status, optionsHeaders: options.headers });

  // ---------- E4: a websocket from a foreign Origin, and what the first listing does ----------
  const wsUrl = `ws://127.0.0.1:${port}/${wsGuid}`;
  const A = new DashClient(wsUrl, { origin: 'https://evil.example' });
  const tOpen = Date.now();
  const openA = await withTimeout(A.open(), 10000, 'open A');
  log('E4 ws open with Origin https://evil.example ->', openA);
  const sessionsA = await A.waitFor(e => e.method === 'sessions' && e.params.sessions.length > 0, 20000);
  const firstSessionsMs = Date.now() - tOpen;
  await sleep(1500);
  const scratchAfterList = fs.readdirSync(REG).sort();
  const deadLeft = dead.filter(g => scratchAfterList.includes(g));
  log('E4 first non-empty sessions after', firstSessionsMs, 'ms:', sessionsA && sessionsA.params.sessions.map(s => s.title));
  log('E4 scratch registry after the listing:', scratchAfterList, 'dead left:', deadLeft.length);
  const sessionKeys = sessionsA ? Object.keys(sessionsA.params.sessions[0]) : [];
  const clientInfoKeys = sessionsA ? Object.keys(sessionsA.params.clientInfo || {}) : [];
  log('E4 a session entry carries keys', sessionKeys, 'and clientInfo carries', clientInfoKeys);
  record('E4', { openWithForeignOrigin: openA, firstSessionsMs, titles: sessionsA && sessionsA.params.sessions.map(s => s.title),
    scratchAfterList, deadLeft: deadLeft.length, sessionKeys, clientInfoKeys,
    homeDirExposed: !!(sessionsA && sessionsA.params.clientInfo && sessionsA.params.clientInfo.homeDir) });

  // ---------- E5: what merely LISTING does to the owner's page ----------
  const tabsA = await A.waitFor(e => e.method === 'tabs' && e.params.tabs.length > 0, 10000);
  await sleep(1000);
  const iconHitsWhileListed = hits['/icon.png'] || 0;
  log('E5 tabs:', tabsA && tabsA.params.tabs.map(t => ({ title: t.title, url: t.url, hasFavicon: !!t.faviconUrl })));
  log('E5 /icon.png requests: before dashboard', hitsBeforeDashboard['/icon.png'] || 0, 'after listing', iconHitsWhileListed);
  const ownerTab = tabsA.params.tabs.find(t => t.url === PAGE);
  // An ordinary navigation in the owner: how many more favicon fetches does the listing cause?
  const beforeNav = hits['/icon.png'] || 0;
  await page.goto(PAGE + '?again');
  await sleep(2500);
  const afterNav = hits['/icon.png'] || 0;
  log('E5 after one owner navigation: /icon.png requests', beforeNav, '->', afterNav);
  record('E5', { iconHitsBeforeDashboard: hitsBeforeDashboard['/icon.png'] || 0, iconHitsAfterListing: iconHitsWhileListed, iconHitsAroundOneOwnerNavigation: [beforeNav, afterNav], allHits: { ...hits } });

  // ---------- E6..E9: attach, watch, drive -- and does the owner's own trace see any of it? ----------
  await ctx.tracing.start({ snapshots: true, screenshots: false });
  const tabsNow = (await A.waitFor(e => e.method === 'tabs' && e.params.tabs.some(t => t.url.startsWith(PAGE)), 5000)).params.tabs;
  const target = tabsNow.find(t => t.url.startsWith(PAGE));
  const tSel = Date.now();
  const sel = await A.call('selectTab', { browser: target.browser, context: target.context, page: target.page });
  const firstFrame = await A.waitFor(e => e.method === 'frame', 10000);
  const frameMs = Date.now() - tSel;
  await sleep(3000);
  const frames3s = A.count('frame', firstFrame ? firstFrame.at : undefined);
  const fb = firstFrame ? Buffer.from(firstFrame.params.data, 'base64') : null;
  log('E6 selectTab ->', sel, 'first frame after', frameMs, 'ms; frames in ~3 s:', frames3s, '; first frame', fb && fb.length, 'bytes, jpeg:', fb && fb[0] === 0xff && fb[1] === 0xd8, 'viewport', firstFrame && [firstFrame.params.viewportWidth, firstFrame.params.viewportHeight]);
  if (fb) fs.writeFileSync(path.join(OUT, 'screencast-frame.jpg'), fb);
  record('E6', { selectTab: sel, firstFrameMs: frameMs, framesIn3s: frames3s, firstFrameBytes: fb && fb.length, isJpeg: !!(fb && fb[0] === 0xff && fb[1] === 0xd8) });

  // Drive it: type into the owner's input, then navigate it elsewhere.
  for (const ch of 'hi') { await A.call('keydown', { key: ch }); await A.call('keyup', { key: ch }); }
  await sleep(300);
  const typed = await page.evaluate(() => document.querySelector('#q') && document.querySelector('#q').value).catch(e => 'ERR ' + e.message);
  const nav = await A.call('navigate', { url: `http://127.0.0.1:${pagePort}/other` });
  await sleep(1000);
  const ownerUrlAfter = page.url();
  const ownerTitleAfter = await page.title();
  const shot = await A.call('screenshot', {});
  const shotBytes = shot.result ? Buffer.from(shot.result.data, 'base64').length : 0;
  const aria = shot.result ? String(shot.result.ariaSnapshot).slice(0, 200) : null;
  log('E7 typed via dashboard, owner reads input value:', JSON.stringify(typed));
  log('E7 navigate ->', nav, '; owner page.url() now', ownerUrlAfter, 'title', ownerTitleAfter);
  log('E7 screenshot ->', shotBytes, 'bytes png; aria:', aria);
  record('E7', { ownerInputValueAfterDashboardKeys: typed, navigateResult: nav, ownerUrlAfter, ownerTitleAfter, screenshotPngBytes: shotBytes, ariaExcerpt: aria });

  const pagesBefore = ctx.pages().length;
  const nt = await A.call('newTab', { browser: target.browser, context: target.context });
  await sleep(1000);
  const pagesAfter = ctx.pages().length;
  log('E9 newTab ->', nt, '; owner context pages', pagesBefore, '->', pagesAfter);
  record('E9', { newTab: nt, ownerPagesBefore: pagesBefore, ownerPagesAfter: pagesAfter });

  // The owner's own action, for contrast in the trace.
  const ownPage = ctx.pages()[0];
  await ownPage.goto(PAGE + '?owner-own-goto');
  const tracePath = path.join(OUT, 'owner-trace.zip');
  await ctx.tracing.stop({ path: tracePath });
  log('E8 owner trace written', tracePath, fs.statSync(tracePath).size, 'bytes');
  record('E8', { tracePath, traceBytes: fs.statSync(tracePath).size });

  // ---------- E10: a pause requested from the dashboard, then the dashboard goes away ----------
  // Re-attach to the owner's first page first (the newTab above moved the attachment).
  const tabs2 = (await A.waitFor(e => e.method === 'tabs' && e.params.tabs.length >= 2, 5000)).params.tabs;
  const first = tabs2.find(t => t.page === target.page) || tabs2[0];
  await A.call('selectTab', { browser: first.browser, context: first.context, page: first.page });
  const pauseResp = await A.call('debuggerPause', {});
  log('E10 debuggerPause ->', pauseResp);
  await A.close();
  log('E10 dashboard connection A closed WITHOUT resuming');
  await sleep(1500);
  const tEval = Date.now();
  const evalWhileGone = await withTimeout(ownPage.evaluate(() => 40 + 2), 12000, 'owner evaluate after the dashboard left');
  log('E10 owner page.evaluate after the dashboard left ->', evalWhileGone, 'after', Date.now() - tEval, 'ms');
  let resumedBy = null; let evalAfterResume = null;
  if (evalWhileGone.timedOut) {
    // Recover from the owner side the way @playwright/mcp's browser_resume does.
    const r = await withTimeout(ctx.debugger.resume(), 5000, 'owner-side resume');
    resumedBy = r;
    evalAfterResume = await withTimeout(ownPage.evaluate(() => 43), 10000, 'evaluate after resume');
    log('E10 owner-side ctx.debugger.resume() ->', r, '; next evaluate ->', evalAfterResume);
  }
  record('E10', { pauseResp, evalAfterDashboardLeft: evalWhileGone, ownerResume: resumedBy, evalAfterResume });

  // ---------- E11: two viewers; one leaves; does the other still hear anything? ----------
  const B = new DashClient(wsUrl, {});
  const D = new DashClient(wsUrl, {});
  await withTimeout(B.open(), 10000, 'open B');
  const bSessions = await B.waitFor(e => e.method === 'sessions' && e.params.sessions.length > 0, 20000);
  await withTimeout(D.open(), 10000, 'open D');
  const dSessions = await D.waitFor(e => e.method === 'sessions' && e.params.sessions.length > 0, 20000);
  log('E11 B listed', !!bSessions, '; D listed', !!dSessions);
  await B.close();
  log('E11 B closed; D stays');
  await sleep(1500);
  // Cause a registry change D ought to be told about: a second bound browser.
  const ctx2 = await pw.chromium.launchPersistentContext(path.join(C, 'profile-owner2'), { headless: true, handleSIGINT: false, handleSIGTERM: false });
  toStop.push(() => ctx2.close().catch(() => {}));
  await ctx2.browser().bind('zoomout-c-second', { workspaceDir: C });
  const tChange = Date.now();
  const dSaw = await D.waitFor(e => e.method === 'sessions' && e.at >= tChange - t0 && e.params.sessions.some(s => s.title === 'zoomout-c-second'), 10000);
  log('E11 after B left, did D see the second browser arrive within 10 s?', !!dSaw);
  // Control: a FRESH connection sees it.
  const E = new DashClient(wsUrl, {});
  await withTimeout(E.open(), 10000, 'open E');
  const eSaw = await E.waitFor(e => e.method === 'sessions' && e.params.sessions.some(s => s.title === 'zoomout-c-second'), 20000);
  log('E11 control: a fresh connection E sees the second browser?', !!eSaw);
  record('E11', { bListed: !!bSessions, dListed: !!dSessions, dSawArrivalAfterBLeft: !!dSaw, freshConnectionSawIt: !!eSaw });
  await D.close();
  await E.close();
  await sleep(1000);

  // ---------- E12: the real UI in a headless tab, reloaded ----------
  const viewer = await pw.chromium.launch({ headless: true });
  toStop.push(() => viewer.close().catch(() => {}));
  const vctx = await viewer.newContext({ viewport: { width: 1400, height: 900 } });
  const vp = await vctx.newPage();
  const hasList = async () => (await vp.evaluate(() => document.body.innerText).catch(() => '')).includes('zoomout-c-owner');
  const waitList = async (ms) => { const s = Date.now(); while (Date.now() - s < ms) { if (await hasList()) return Date.now() - s; await sleep(200); } return null; };
  await vp.goto(url);
  const firstLoad = await waitList(15000);
  await vp.screenshot({ path: path.join(OUT, 'ui-first-load.png') });
  log('E12 first load: list shown after', firstLoad, 'ms');
  const reloads = [];
  for (let i = 0; i < 10; i++) {
    await vp.reload();
    const shown = await waitList(8000);
    reloads.push(shown);
    log('E12 reload', i + 1, '-> list shown after', shown, 'ms');
    if (shown === null && !fs.existsSync(path.join(OUT, 'ui-after-reload-empty.png')))
      await vp.screenshot({ path: path.join(OUT, 'ui-after-reload-empty.png') });
  }
  // Two tabs: open a second, close the first, and see whether the second hears a new browser.
  const vp2 = await vctx.newPage();
  await vp2.goto(url);
  const vp2First = await (async () => { const s = Date.now(); while (Date.now() - s < 15000) { if ((await vp2.evaluate(() => document.body.innerText).catch(() => '')).includes('zoomout-c-owner')) return Date.now() - s; await sleep(200); } return null; })();
  await vp.close();
  await sleep(1500);
  const ctx3 = await pw.chromium.launchPersistentContext(path.join(C, 'profile-owner3'), { headless: true, handleSIGINT: false, handleSIGTERM: false });
  toStop.push(() => ctx3.close().catch(() => {}));
  await ctx3.browser().bind('zoomout-c-third', { workspaceDir: C });
  const tab2Heard = await (async () => { const s = Date.now(); while (Date.now() - s < 10000) { if ((await vp2.evaluate(() => document.body.innerText).catch(() => '')).includes('zoomout-c-third')) return Date.now() - s; await sleep(250); } return null; })();
  await vp2.screenshot({ path: path.join(OUT, 'ui-second-tab-after-first-closed.png') });
  log('E12 second tab listed after', vp2First, 'ms; after the first tab closed, it showed a newly bound browser after', tab2Heard, 'ms (null = never within 10 s)');
  record('E12', { firstLoadMs: firstLoad, reloadsMs: reloads, reloadsListed: reloads.filter(x => x !== null).length, reloadsTried: reloads.length,
    secondTabFirstMs: vp2First, secondTabHeardNewBrowserAfterFirstClosedMs: tab2Heard });
  await vp2.close();

  // ---------- E13: close the owner's browser from the dashboard ----------
  const F = new DashClient(wsUrl, {});
  await withTimeout(F.open(), 10000, 'open F');
  await F.waitFor(e => e.method === 'sessions' && e.params.sessions.some(s => s.title === 'zoomout-c-owner'), 20000);
  let disconnectedAt = null;
  browser.once('disconnected', () => { disconnectedAt = Date.now(); });
  const tClose = Date.now();
  const closeResp = await withTimeout(F.call('closeSession', { browser: ownerGuid }), 15000, 'closeSession');
  await sleep(1500);
  log('E13 closeSession(owner) ->', closeResp, '; owner browser disconnected:', disconnectedAt ? (disconnectedAt - tClose) + ' ms' : 'no', '; isConnected', browser.isConnected());
  const scratchAfterClose = fs.readdirSync(REG).sort();
  log('E13 scratch registry after the close:', scratchAfterClose);
  record('E13', { closeSession: closeResp, ownerDisconnectedMs: disconnectedAt ? disconnectedAt - tClose : null, ownerIsConnected: browser.isConnected(), scratchAfterClose });
  await F.close();

  // ---------- E14: cleanup, and the real registry afterwards ----------
  await cleanup();
  await sleep(1500);
  const realAfter = realListing();
  fs.writeFileSync(path.join(OUT, 'real-registry-after-probe.txt'), realAfter.join('\n') + '\n');
  const missing = realBefore.filter(n => !realAfter.includes(n));
  log('E14 real registry after:', realAfter.length, '; entries that existed before and are missing now:', missing.length, missing);
  log('E14 dashboard child exitCode', dashboardChild.exitCode, 'killed', dashboardChild.killed);
  record('E14', { realBefore: realBefore.length, realAfter: realAfter.length, missingFromBefore: missing, dashboardExitCode: dashboardChild.exitCode });
  clearTimeout(watchdog);
  process.exit(0);
})().catch(async (e) => {
  log('FATAL', String(e && e.stack || e));
  await cleanup();
  const realAfter = realListing();
  fs.writeFileSync(path.join(OUT, 'real-registry-after-probe.txt'), realAfter.join('\n') + '\n');
  process.exit(1);
});
