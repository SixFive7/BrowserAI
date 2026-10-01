// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Track C probe 3: the dashboard is a per-USER singleton even with --port. A second `--port`
// dashboard started by anyone else under the same user connects to the first and exits without
// serving; a `--kill` from anyone stops the first. Measured with both instances on --port (so no
// window can appear anywhere), on a singleton pipe name moved into scratch by PWTEST_SOCKETS_DIR.
'use strict';
const fs = require('fs');
const path = require('path');
const http = require('http');
const { spawn } = require('child_process');
const REPO = 'C:/Source/SixFive7/BrowserAI';
const C = path.join(REPO, '.work', 'zoomout', 'c');
const PWLIB = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const REG = process.env.PWTEST_SERVER_REGISTRY, SOCK = process.env.PWTEST_SOCKETS_DIR;
const under = (p) => p && path.resolve(p).toLowerCase().startsWith(path.resolve(C).toLowerCase() + path.sep);
if (!under(REG) || !under(SOCK)) { console.error('REFUSING'); process.exit(2); }
const TMP = path.join(C, 'tmp'); fs.mkdirSync(TMP, { recursive: true }); process.env.TEMP = TMP; process.env.TMP = TMP;
fs.mkdirSync(REG, { recursive: true });
const OUT = path.join(C, 'evidence', 'probe3.log'); fs.writeFileSync(OUT, '');
const t0 = Date.now();
const log = (...a) => { const l = `[+${String(Date.now() - t0).padStart(6)} ms] ` + a.map(x => typeof x === 'string' ? x : JSON.stringify(x)).join(' '); fs.appendFileSync(OUT, l + '\n'); console.log(l); };
const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const entry = path.join(PWLIB, 'lib', 'entry', 'dashboardApp.js');
const started = [];
function start(args) {
  const p = spawn(process.execPath, [entry, ...args], { env: { ...process.env }, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  started.push(p);
  const rec = { p, out: '', err: '', exit: null };
  p.stdout.on('data', d => { rec.out += String(d); });
  p.stderr.on('data', d => { rec.err += String(d); });
  p.on('exit', (code, sig) => { rec.exit = { code, sig, at: Date.now() - t0 }; });
  return rec;
}
const get = (port) => new Promise(r => { const q = http.get({ host: '127.0.0.1', port, path: '/' }, res => { r(res.statusCode); res.resume(); }); q.on('error', e => r('ERR ' + e.code)); });
(async () => {
  const d1 = start(['--port=0', '--host=127.0.0.1']);
  let s = Date.now(); while (!/Listening on/.test(d1.out) && Date.now() - s < 20000) await sleep(50);
  const url1 = (d1.out.match(/Listening on (\S+)/) || [])[1];
  log('D1 pid', d1.p.pid, 'says', d1.out.trim(), '; GET / ->', url1 ? await get(Number(new URL(url1).port)) : 'n/a');
  // A second --port dashboard, as a second product or a user's `playwright-cli show --port` would start it.
  const d2 = start(['--port=0', '--host=127.0.0.1']);
  s = Date.now(); while (d2.exit === null && Date.now() - s < 20000) await sleep(50);
  log('D2 pid', d2.p.pid, 'stdout:', JSON.stringify(d2.out.trim()), 'stderr:', JSON.stringify(d2.err.trim()), 'exit:', d2.exit);
  log('D1 still serving after D2?', url1 ? await get(Number(new URL(url1).port)) : 'n/a', '; D1 exit', d1.exit);
  // A `--kill`, as `playwright-cli show --kill` sends it.
  const d3 = start(['--kill']);
  s = Date.now(); while ((d3.exit === null || d1.exit === null) && Date.now() - s < 20000) await sleep(50);
  log('D3 (--kill) exit:', d3.exit, '; D1 exit after the kill:', d1.exit, '; GET / on D1 ->', url1 ? await get(Number(new URL(url1).port)) : 'n/a');
  for (const p of started) { try { p.stdin.end(); } catch {} try { if (p.exitCode === null) p.kill(); } catch {} }
  await sleep(500);
  log('all stopped:', started.map(p => ({ pid: p.pid, exitCode: p.exitCode, killed: p.killed })));
  process.exit(0);
})().catch(e => { log('FATAL', String(e && e.stack || e)); for (const p of started) { try { p.kill(); } catch {} } process.exit(1); });
