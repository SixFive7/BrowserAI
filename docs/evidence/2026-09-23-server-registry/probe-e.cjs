// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Phase E: cost and race, separated.
//   E1  one process, registry seeded to the REAL size -> how long does list() take,
//       and where does the time go (watcher ready vs connect probes vs unlinks)?
//   E2  8 concurrent list() processes over a SMALL registry holding one LIVE
//       descriptor -> is a live descriptor ever reaped by a concurrent caller?
// Everything runs inside PWTEST_SERVER_REGISTRY. The real directory is read only.
const fs = require('fs');
const path = require('path');
const net = require('net');
const { fork } = require('child_process');
const REPO = 'C:/Source/SixFive7/BrowserAI';
const SCRATCH = path.join(REPO, '.work', 't7-2026-09-23');
const PWLIB = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const pw = require(PWLIB);
const { serverRegistry } = require(path.join(PWLIB, 'lib', 'serverRegistry.js'));

const REAL = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'b');
const REG = process.env.PWTEST_SERVER_REGISTRY;

if (process.argv[2] === '--child') {
  (async () => {
    const t = Date.now();
    const r = await serverRegistry.list();
    process.send({ ms: Date.now() - t, connectable: [...r.values()].flat().map(d => d.browser.guid) });
    process.exit(0);
  })().catch(e => { try { process.send({ error: e.message }); } catch {} process.exit(1); });
  return;
}

const seed = (n) => {
  fs.rmSync(REG, { recursive: true, force: true });
  fs.mkdirSync(REG, { recursive: true });
  const all = fs.readdirSync(REAL);
  const take = n === null ? all : all.slice(0, n);
  for (const f of take) fs.copyFileSync(path.join(REAL, f), path.join(REG, f));
  return take.length;
};

(async () => {
  const realStart = fs.readdirSync(REAL).length;

  // -------- E1: one process, real-size registry --------
  const seeded = seed(null);
  console.log('=== E1: single process, registry seeded with ' + seeded + ' copies (real dir: ' + realStart + ') ===');

  // Instrument the three phases by hand, same code path as list().
  const t0 = Date.now();
  const dispose = serverRegistry.watch();
  await serverRegistry.ready();
  const tReady = Date.now();
  console.log('  watcher ready over ' + seeded + ' files : ' + (tReady - t0) + ' ms');

  // Raw connect-probe cost, same shape as canConnectTo, over 200 endpoints.
  const sample = fs.readdirSync(REG).slice(0, 200).map(n => JSON.parse(fs.readFileSync(path.join(REG, n), 'utf-8')).endpoint);
  const tc0 = Date.now();
  await Promise.all(sample.map(ep => new Promise(res => {
    const s = net.createConnection(ep, () => { s.destroy(); res(true); });
    s.on('error', () => res(false));
  })));
  console.log('  200 dead-endpoint connect probes   : ' + (Date.now() - tc0) + ' ms');
  dispose();

  const before = fs.readdirSync(REG).length;
  const tl0 = Date.now();
  const listed = await serverRegistry.list();
  const listMs = Date.now() - tl0;
  const after = fs.readdirSync(REG).length;
  console.log('  list() over ' + before + ' entries        : ' + listMs + ' ms');
  console.log('  connectable returned               : ' + [...listed.values()].flat().length);
  console.log('  reaped                             : ' + (before - after) + '  (left: ' + after + ')');

  // -------- E2: concurrency race against a LIVE descriptor --------
  const small = seed(40);
  console.log('\n=== E2: 8 concurrent list() over ' + small + ' dead + 1 live ===');
  const profile = path.join(SCRATCH, 'profile-e');
  fs.mkdirSync(profile, { recursive: true });
  const context = await pw.chromium.launchPersistentContext(profile, {
    headless: true, handleSIGINT: false, handleSIGTERM: false,
  });
  const browser = context.browser();
  await browser.bind('t7-race-live', { workspaceDir: SCRATCH });
  const liveGuid = browser._guid;
  console.log('  LIVE guid: ' + liveGuid);
  console.log('  count BEFORE: ' + fs.readdirSync(REG).length);

  const N = 8;
  const t2 = Date.now();
  const results = await Promise.all(Array.from({ length: N }, () => new Promise(res => {
    const c = fork(__filename, ['--child'], { env: { ...process.env } });
    const timer = setTimeout(() => { c.kill('SIGKILL'); res({ timedOut: true }); }, 120000);
    c.on('message', m => { clearTimeout(timer); res(m); });
    c.on('exit', code => { clearTimeout(timer); res({ exit: code }); });
  })));
  const wall = Date.now() - t2;
  const survivors = fs.readdirSync(REG);
  console.log('  ' + N + ' concurrent list() finished in ' + wall + ' ms wall');
  results.forEach((r, i) => console.log('    child ' + i + ': ' + JSON.stringify(r)));
  console.log('  count AFTER : ' + survivors.length + ' -> ' + JSON.stringify(survivors));
  console.log('  LIVE descriptor survived ' + N + ' concurrent reaps: ' + survivors.includes(liveGuid));

  const page = context.pages()[0] || await context.newPage();
  await page.goto('data:text/html,<h1 id=x>survived</h1>');
  console.log('  live browser after ' + N + ' concurrent probes: ' + JSON.stringify(await page.textContent('#x'))
    + '  isConnected=' + browser.isConnected());
  await context.close();
  await browser.close();

  console.log('\nREAL directory at start: ' + realStart + '   at end: ' + fs.readdirSync(REAL).length);
})().then(() => process.exit(0), e => { console.error('FATAL', e); process.exit(1); });
