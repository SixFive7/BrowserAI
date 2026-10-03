// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch probe: with a short --idle-timeout, does a pending browser_resume
// end when the idle timer closes the browser, and what does the session look
// like afterwards? Headless; LOCALAPPDATA, TEMP and the browsers path in scratch.
// Usage: node resume-idle.cjs <@playwright/mcp package dir> <outdir>
'use strict';
const fs = require('fs');
const path = require('path');
const http = require('http');
const { spawn } = require('child_process');

const [MCPDIR, OUT] = process.argv.slice(2);
const SCRATCH = path.resolve(__dirname, '..');
const under = (p) => !!p && path.resolve(p).toLowerCase().startsWith(SCRATCH.toLowerCase() + path.sep);
for (const k of ['LOCALAPPDATA', 'TEMP', 'TMP', 'PLAYWRIGHT_BROWSERS_PATH', 'PWTEST_SOCKETS_DIR']) {
  if (!under(process.env[k])) { console.error(`REFUSING: ${k} is not under ${SCRATCH}`); process.exit(2); }
}
fs.mkdirSync(OUT, { recursive: true });
const LOG = path.join(OUT, 'resume-idle.log');
fs.writeFileSync(LOG, '');
const t0 = Date.now();
const log = (...a) => fs.appendFileSync(LOG, `[+${String(Date.now() - t0).padStart(6)} ms] ` + a.map((x) => (typeof x === 'string' ? x : JSON.stringify(x))).join(' ') + '\n');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const bounded = (p, ms) => Promise.race([p.then((r) => ({ ok: true, ...r })), sleep(ms).then(() => ({ ok: false, timedOut: true, ms }))]);
const short = (s, n = 300) => String(s).replace(/\r?\n/g, ' | ').slice(0, n);

const server = http.createServer((req, res) => { res.writeHead(200, { 'content-type': 'text/html' }); res.end(`<title>${req.url}</title><h1>${req.url}</h1>`); });

(async () => {
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  const base = `http://127.0.0.1:${server.address().port}`;
  const proc = spawn(process.execPath, [path.join(MCPDIR, 'cli.js'), '--headless', '--browser=chromium', '--caps=devtools', '--idle-timeout=15000', `--user-data-dir=${path.join(OUT, 'profile')}`, `--output-dir=${path.join(OUT, 'out')}`], { cwd: OUT, env: { ...process.env }, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  let buf = ''; const pending = new Map(); let id = 1;
  proc.stdout.on('data', (d) => { buf += d; let i; while ((i = buf.indexOf('\n')) >= 0) { const line = buf.slice(0, i); buf = buf.slice(i + 1); try { const m = JSON.parse(line); if (pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } } catch {} } });
  const send = (method, params) => { const n = id++; const sentAt = Date.now() - t0; return new Promise((r) => { pending.set(n, (v) => r({ v, sentAt, answeredAt: Date.now() - t0 })); proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: n, method, params }) + '\n'); }); };
  const call = (name, args) => send('tools/call', { name, arguments: args || {} });
  const text = (r) => { try { return r.v.result.content.filter((c) => c.type === 'text').map((c) => c.text).join('\n'); } catch { return JSON.stringify(r); } };
  await send('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'resume-idle', version: '0' } });
  proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const nav = await call('browser_navigate', { url: `${base}/start` });
  log('navigate /start', short(text(nav), 120));
  const arm = await call('browser_run_code_unsafe', { code: 'async (page) => { setTimeout(() => page.context().debugger.requestPause().catch(() => {}), 2000); return "armed"; }' });
  log('arm', short(text(arm), 80));
  await sleep(3000);
  const parked = call('browser_navigate', { url: `${base}/parked` });
  await sleep(2000);
  const resume = call('browser_resume', {});
  const p = await bounded(parked, 10000);
  log('parked navigate answered after resume?', p.ok, p.ok ? `at +${p.answeredAt}` : '');
  const r = await bounded(resume, 60000);
  log('browser_resume answered within 60 s?', r.ok, r.ok ? `at +${r.answeredAt} ms, sent at +${r.sentAt} ms, text: ${short(text(r), 200)}` : '');
  const tabs = await bounded(call('browser_tabs', { action: 'list' }), 30000);
  log('browser_tabs afterwards ->', tabs.ok ? short(text(tabs), 200) : tabs);
  proc.stdin.end();
  await new Promise((res) => { proc.on('exit', res); setTimeout(res, 8000); });
  server.close();
  setTimeout(() => process.exit(0), 300);
})().catch((e) => { log('FATAL', String(e && e.stack || e)); process.exit(1); });
