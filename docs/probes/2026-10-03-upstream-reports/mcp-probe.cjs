// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch probe for two @playwright/mcp drafts:
//  (2) every browser the server starts is bound into the per-user registry,
//      and a second Playwright tool can attach to it by name and drive it;
//  (3) browser_resume after a pause armed in a long-lived context.
// Everything is headless. LOCALAPPDATA, TEMP and PLAYWRIGHT_BROWSERS_PATH point
// into scratch, so the "default" registry here is a scratch directory too.
// Usage: node mcp-probe.cjs <@playwright/mcp package dir> <outdir>
'use strict';
const fs = require('fs');
const path = require('path');
const http = require('http');
const { spawn, execFileSync } = require('child_process');

const [MCPDIR, OUT] = process.argv.slice(2);
const SCRATCH = path.resolve(__dirname, '..');
const under = (p) => !!p && path.resolve(p).toLowerCase().startsWith(SCRATCH.toLowerCase() + path.sep);
for (const k of ['LOCALAPPDATA', 'TEMP', 'TMP', 'PLAYWRIGHT_BROWSERS_PATH', 'PWTEST_SOCKETS_DIR']) {
  if (!under(process.env[k])) { console.error(`REFUSING: ${k}=${process.env[k]} is not under ${SCRATCH}`); process.exit(2); }
}
if (process.env.PWTEST_SERVER_REGISTRY) { console.error('REFUSING: PWTEST_SERVER_REGISTRY must be unset for this probe'); process.exit(2); }
const REG = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'b');
const CLI = path.join(MCPDIR, 'cli.js');
fs.mkdirSync(OUT, { recursive: true });
const LOG = path.join(OUT, 'mcp-probe.log');
fs.writeFileSync(LOG, '');
const t0 = Date.now();
const log = (...a) => { const line = `[+${String(Date.now() - t0).padStart(6)} ms] ` + a.map((x) => (typeof x === 'string' ? x : JSON.stringify(x))).join(' '); fs.appendFileSync(LOG, line + '\n'); };
const results = { versions: { mcp: require(path.join(MCPDIR, 'package.json')).version, node: process.version }, experiments: {} };
const record = (k, v) => { results.experiments[k] = v; fs.writeFileSync(path.join(OUT, 'mcp-probe-results.json'), JSON.stringify(results, null, 2)); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const watchdog = setTimeout(() => { log('WATCHDOG 6 min'); stopAll().then(() => process.exit(3)); }, 6 * 60 * 1000);

const pageServer = http.createServer((req, res) => {
  const u = new URL(req.url, 'http://x');
  if (u.pathname === '/favicon.ico') { res.writeHead(404); return res.end(); }
  res.writeHead(200, { 'content-type': 'text/html' });
  res.end(`<title>${u.pathname}</title><h1>${u.pathname}</h1>`);
});

const children = [];
class Mcp {
  constructor(label, args) {
    this.label = label;
    this.proc = spawn(process.execPath, [CLI, ...args], { cwd: OUT, env: { ...process.env }, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    this.nextId = 1; this.pending = new Map(); this.buf = ''; this.stderr = '';
    this.proc.stdout.on('data', (d) => {
      this.buf += String(d);
      let i;
      while ((i = this.buf.indexOf('\n')) >= 0) {
        const line = this.buf.slice(0, i); this.buf = this.buf.slice(i + 1);
        if (!line.trim()) continue;
        let msg; try { msg = JSON.parse(line); } catch { continue; }
        if (msg.id !== undefined && this.pending.has(msg.id)) { const r = this.pending.get(msg.id); this.pending.delete(msg.id); r(msg); }
      }
    });
    this.proc.stderr.on('data', (d) => { this.stderr += String(d); });
    children.push(this);
  }
  send(method, params) { const id = this.nextId++; const sentAt = Date.now() - t0; const p = new Promise((r) => { this.pending.set(id, r); this.proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n'); }); return p.then((v) => ({ v, sentAt, answeredAt: Date.now() - t0 })); }
  notify(method, params) { this.proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', method, params }) + '\n'); }
  call(name, args) { return this.send('tools/call', { name, arguments: args || {} }); }
  async init(name) { const r = await bounded(this.send('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name, version: '0' } }), 30000); this.notify('notifications/initialized', {}); return r; }
  async stop() { try { this.proc.stdin.end(); } catch {} for (let i = 0; i < 30 && this.proc.exitCode === null; i++) await sleep(200); if (this.proc.exitCode === null) { try { this.proc.kill(); } catch {} } }
}
const text = (r) => { try { return r.v.result.content.filter((c) => c.type === 'text').map((c) => c.text).join('\n'); } catch { return JSON.stringify(r); } };
const short = (s, n = 400) => String(s).replace(/\r?\n/g, ' | ').slice(0, n);
const bounded = (p, ms) => Promise.race([p.then((r) => ({ ok: true, ...r })), sleep(ms).then(() => ({ ok: false, timedOut: true, ms }))]);
async function stopAll() { for (const c of children.reverse()) await c.stop(); children.length = 0; }
const registry = () => { try { return fs.readdirSync(REG).sort(); } catch { return []; } };

(async () => {
  await new Promise((r) => pageServer.listen(0, '127.0.0.1', r));
  const base = `http://127.0.0.1:${pageServer.address().port}`;
  const help = execFileSync(process.execPath, [CLI, '--help'], { encoding: 'utf8', windowsHide: true });
  fs.writeFileSync(path.join(OUT, 'mcp-help.txt'), help);
  record('help', { lines: help.split(/\r?\n/).length, mentionsBindOrRegistry: /\bbind\b(?! server)|registry|dashboard|discover/i.test(help.replace(/host to bind server to/ig, '')) });
  log('E0 --help lines', help.split(/\r?\n/).length, 'registry dir', REG, 'before', registry());

  const common = ['--headless', '--browser=chromium', '--caps=devtools', `--output-dir=${path.join(OUT, 'out')}`];
  // ---- E1: server A, a client named probe-a ----
  const a = new Mcp('A', [...common, `--user-data-dir=${path.join(OUT, 'profile-a')}`]);
  const initA = await a.init('probe-a');
  log('E1 A initialize', initA.ok ? initA.v.result.serverInfo : initA);
  const list = await bounded(a.send('tools/list', {}), 30000);
  const names = list.ok ? list.v.result.tools.map((t) => t.name) : [];
  record('E1', { serverInfo: initA.ok ? initA.v.result.serverInfo : null, tools: names.length, hasResume: names.includes('browser_resume'), hasRunCode: names.includes('browser_run_code_unsafe') });
  log('E1 tools', names.length, 'browser_resume', names.includes('browser_resume'), 'browser_run_code_unsafe', names.includes('browser_run_code_unsafe'));
  const nav1 = await bounded(a.call('browser_navigate', { url: `${base}/one` }), 90000);
  log('E2 A navigate /one ->', nav1.ok ? short(text(nav1), 200) : nav1);
  const reg = registry();
  const desc = reg.length ? JSON.parse(fs.readFileSync(path.join(REG, reg[reg.length - 1]), 'utf8')) : null;
  record('E2', { registryDir: REG, entries: reg, title: desc && desc.title, endpoint: desc && desc.endpoint, keys: desc && Object.keys(desc), browserKeys: desc && Object.keys(desc.browser || {}), userDataDir: desc && desc.browser && desc.browser.userDataDir });
  log('E2 registry after first navigate', reg, 'title', desc && desc.title, 'endpoint', desc && desc.endpoint);

  // ---- E3: server B attaches to A's browser by its registry title and drives it ----
  const b = new Mcp('B', ['--endpoint=probe-a', `--output-dir=${path.join(OUT, 'out-b')}`]);
  const initB = await b.init('probe-b');
  log('E3 B initialize', initB.ok ? initB.v.result.serverInfo : initB);
  const tabsB = await bounded(b.call('browser_tabs', { action: 'list' }), 60000);
  log('E3 B browser_tabs ->', tabsB.ok ? short(text(tabsB), 300) : tabsB);
  const navB = await bounded(b.call('browser_navigate', { url: `${base}/driven-by-b` }), 60000);
  log('E3 B browser_navigate /driven-by-b ->', navB.ok ? short(text(navB), 200) : navB);
  const tabsA = await bounded(a.call('browser_tabs', { action: 'list' }), 60000);
  log('E3 A browser_tabs after B navigated ->', tabsA.ok ? short(text(tabsA), 300) : tabsA);
  record('E3', { bTabs: tabsB.ok ? text(tabsB) : tabsB, bNavigate: navB.ok ? short(text(navB), 300) : navB, aTabsAfter: tabsA.ok ? text(tabsA) : tabsA });
  const armFromB = process.env.ARM_FROM_B === '1';
  if (!armFromB) {
    await b.stop();
    log('E3 B stopped, exit', b.proc.exitCode);
    const tabsA2 = await bounded(a.call('browser_tabs', { action: 'list' }), 60000);
    log('E3 A browser_tabs after B exited ->', tabsA2.ok ? short(text(tabsA2), 300) : tabsA2);
  }

  // ---- E4: a pause armed through the public Debugger API, then browser_resume ----
  // The pause is armed two seconds after the call returns, so that the call
  // which meets it is an ordinary browser_navigate, as with a pause from the dashboard.
  // With ARM_FROM_B=1 the attached server B arms it, so the pause crosses processes.
  const armer = armFromB ? b : a;
  const arm = await bounded(armer.call('browser_run_code_unsafe', { code: 'async (page) => { setTimeout(() => page.context().debugger.requestPause().catch(() => {}), 2000); return "pause requested in 2 s"; }' }), 20000);
  log(`E4 ${armer.label} run_code (delayed requestPause) ->`, arm.ok ? short(text(arm), 300) : arm);
  await sleep(4000);
  const navP = a.call('browser_navigate', { url: `${base}/after-pause` });
  const navPEarly = await bounded(navP, 10000);
  log('E4 A navigate /after-pause answered within 10 s?', navPEarly.ok);
  const snap = await bounded(a.call('browser_snapshot', {}), 20000);
  const snapText = snap.ok ? text(snap) : '';
  log('E4 A concurrent browser_snapshot ->', snap.ok ? `answered; mentions "### Paused": ${/### Paused/.test(snapText)}` : snap);
  fs.writeFileSync(path.join(OUT, 'snapshot-while-parked.txt'), snapText);
  const resume = a.call('browser_resume', {});
  const navAfterResume = await bounded(navP, 20000);
  log('E4 parked navigate answered after resume?', navAfterResume.ok, navAfterResume.ok ? `at +${navAfterResume.answeredAt} ms, sent at +${navAfterResume.sentAt} ms` : '');
  const resumeEarly = await bounded(resume, 30000);
  log('E4 browser_resume answered within 30 s?', resumeEarly.ok);
  const follow = await bounded(a.call('browser_tabs', { action: 'list' }), 20000);
  log('E4 A concurrent browser_tabs while resume pending ->', follow.ok ? short(text(follow), 200) : follow);
  // Close the browser through A itself, and see whether the pending resume then answers.
  const closeAt = Date.now() - t0;
  const close = await bounded(a.call('browser_close', {}), 30000);
  log('E4 A browser_close ->', close.ok ? short(text(close), 200) : close);
  const resumeLate = await bounded(resume, 15000);
  log('E4 browser_resume answered after browser_close?', resumeLate.ok, resumeLate.ok ? `at +${resumeLate.answeredAt} ms (close sent at +${closeAt} ms): ${short(text(resumeLate), 200)}` : '');
  record('E4', { armOk: arm.ok, armText: arm.ok ? short(text(arm), 300) : null, navigateAnsweredWithin10s: navPEarly.ok, snapshotAnswered: snap.ok, snapshotMentionsPaused: /### Paused/.test(snapText), navigateAnsweredAfterResume: navAfterResume.ok, navigateAnsweredAt: navAfterResume.answeredAt, navigateSentAt: navAfterResume.sentAt, resumeAnsweredWithin30s: resumeEarly.ok, tabsWhileResumePending: follow.ok, closeSentAt: closeAt, resumeAnsweredAfterClose: resumeLate.ok, resumeAnsweredAt: resumeLate.answeredAt || null, resumeText: resumeLate.ok ? text(resumeLate) : null });

  // ---- E5: --isolated binds too ----
  const c = new Mcp('C', [...common, '--isolated']);
  await c.init('probe-isolated');
  const before5 = registry();
  const nav5 = await bounded(c.call('browser_navigate', { url: `${base}/isolated` }), 90000);
  const after5 = registry();
  const added = after5.filter((x) => !before5.includes(x));
  const d5 = added.length ? JSON.parse(fs.readFileSync(path.join(REG, added[0]), 'utf8')) : null;
  log('E5 --isolated navigate ->', nav5.ok ? 'ok' : nav5, 'new descriptors', added, 'title', d5 && d5.title);
  record('E5', { navigateOk: nav5.ok, added, title: d5 && d5.title });

  await stopAll();
  record('end', { registryAtEnd: registry(), stderrA: a.stderr.slice(-2000), stderrC: c.stderr.slice(-2000) });
  log('END registry', registry());
  pageServer.close();
  clearTimeout(watchdog);
  setTimeout(() => process.exit(0), 300);
})().catch(async (e) => { log('FATAL', String(e && e.stack || e)); await stopAll(); process.exit(1); });
