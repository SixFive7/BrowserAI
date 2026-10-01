// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Track C probe 4: Playwright's own TRACE VIEWER, served headlessly for a trace a session
// produced -- the read-only alternative to the dashboard. `show-trace --host --port` serves over
// HTTP; CLAUDECODE=1 is set so upstream's isCodingAgent() suppresses its browser auto-open.
// A headless Chromium launched here loads the page. Nothing is attached to any live browser.
'use strict';
const fs = require('fs');
const path = require('path');
const http = require('http');
const { spawn } = require('child_process');
const REPO = 'C:/Source/SixFive7/BrowserAI';
const C = path.join(REPO, '.work', 'zoomout', 'c');
const PWLIB = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const TRACE = path.join(C, 'evidence', 'owner-trace.zip');
const OUT = path.join(C, 'evidence', 'probe4.log'); fs.writeFileSync(OUT, '');
const TMP = path.join(C, 'tmp'); fs.mkdirSync(TMP, { recursive: true });
process.env.TEMP = TMP; process.env.TMP = TMP;
process.env.PWTEST_SERVER_REGISTRY = path.join(C, 'reg4'); process.env.PWTEST_SOCKETS_DIR = path.join(C, 'sockets4');
const t0 = Date.now();
const log = (...a) => { const l = `[+${String(Date.now() - t0).padStart(6)} ms] ` + a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '); fs.appendFileSync(OUT, l + '\n'); console.log(l); };
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const get = (port, p) => new Promise(r => { const q = http.get({ host: '127.0.0.1', port, path: p }, res => { let n = 0; res.on('data', d => n += d.length); res.on('end', () => r({ status: res.statusCode, location: res.headers.location, bytes: n, headers: res.headers })); }); q.on('error', e => r({ error: e.code })); });
(async () => {
  const srv = spawn(process.execPath, [path.join(PWLIB, 'cli.js'), 'show-trace', '--host', '127.0.0.1', '--port', '0', TRACE],
    { env: { ...process.env, CLAUDECODE: '1' }, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  let out = ''; srv.stdout.on('data', d => out += String(d)); srv.stderr.on('data', d => out += String(d));
  let s = Date.now(); while (!/Listening on/.test(out) && Date.now() - s < 20000) await sleep(50);
  const url = (out.match(/Listening on (\S+)/) || [])[1];
  log('show-trace pid', srv.pid, 'says', out.trim());
  const port = Number(new URL(url).port);
  const root = await get(port, '/');
  const inRoot = await get(port, '/trace/file?path=' + encodeURIComponent(TRACE));
  const outside = await get(port, '/trace/file?path=' + encodeURIComponent('C:\\Windows\\win.ini'));
  const outside2 = await get(port, '/trace/file?path=' + encodeURIComponent(path.join(C, 'rig', 'dashboard-probe.cjs')));
  log('GET / ->', root.status, root.location);
  log('trace file inside the allowed root ->', inRoot.status, inRoot.bytes, 'bytes; C:\\Windows\\win.ini ->', outside.status, '; a sibling directory ->', outside2.status);
  log('index headers', root.headers);
  const pw = require(PWLIB);
  const b = await pw.chromium.launch({ headless: true });
  const p = await (await b.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
  await p.goto(url.replace('localhost', '127.0.0.1'));
  let text = ''; s = Date.now();
  while (Date.now() - s < 20000) { text = await p.evaluate(() => document.body.innerText).catch(() => ''); if (/Navigate|goto|Evaluate/i.test(text)) break; await sleep(300); }
  await p.screenshot({ path: path.join(C, 'evidence', 'trace-viewer.png') });
  log('trace viewer text (first 600 chars):', text.replace(/\s+/g, ' ').slice(0, 600));
  await b.close();
  try { srv.stdin.end(); } catch {} try { srv.kill(); } catch {}
  await sleep(500);
  log('show-trace exit', srv.exitCode, 'killed', srv.killed);
  process.exit(0);
})().catch(e => { log('FATAL', String(e && e.stack || e)); process.exit(1); });
