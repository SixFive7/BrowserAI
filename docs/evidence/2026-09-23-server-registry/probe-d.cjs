// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Phase D: (1) how long does list() take over a registry the real size?
//          (2) does a CONCURRENT list() ever reap a LIVE descriptor?
// Everything happens inside a scratch registry (PWTEST_SERVER_REGISTRY), seeded
// with COPIES of the maintainer's real descriptors. The real directory is only
// ever read.
const fs = require('fs');
const path = require('path');
const { fork } = require('child_process');
const REPO = 'C:/Source/SixFive7/BrowserAI';
const SCRATCH = path.join(REPO, '.work', 't7-2026-09-23');
const PWLIB = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const pw = require(PWLIB);
const { serverRegistry } = require(path.join(PWLIB, 'lib', 'serverRegistry.js'));

const REAL = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'b');
const REG = process.env.PWTEST_SERVER_REGISTRY;

// Child mode: just call list() once and report.
if (process.argv[2] === '--child') {
  (async () => {
    const t = Date.now();
    const r = await serverRegistry.list();
    process.send({ ms: Date.now() - t, connectable: [...r.values()].flat().map(d => d.browser.guid) });
    process.exit(0);
  })().catch(e => { process.send({ error: e.message }); process.exit(1); });
  return;
}

(async () => {
  const realStart = fs.readdirSync(REAL).length;
  fs.rmSync(REG, { recursive: true, force: true });
  fs.mkdirSync(REG, { recursive: true });

  // Seed with COPIES of every real descriptor -> a registry the real size.
  const all = fs.readdirSync(REAL);
  for (const n of all) fs.copyFileSync(path.join(REAL, n), path.join(REG, n));
  console.log('seeded scratch registry with ' + fs.readdirSync(REG).length + ' copies (real dir has ' + realStart + ')');

  // One genuinely LIVE browser bound into the scratch registry.
  const profile = path.join(SCRATCH, 'profile-d');
  fs.mkdirSync(profile, { recursive: true });
  const context = await pw.chromium.launchPersistentContext(profile, {
    headless: true, handleSIGINT: false, handleSIGTERM: false,
  });
  const browser = context.browser();
  await browser.bind('t7-scale-live', { workspaceDir: SCRATCH });
  const liveGuid = browser._guid;
  console.log('LIVE guid: ' + liveGuid);

  const before = fs.readdirSync(REG).length;
  console.log('count BEFORE: ' + before);

  // ---- (2) N concurrent list() calls from N separate processes ----
  const N = 8;
  console.log('\nforking ' + N + ' concurrent list() processes...');
  const t0 = Date.now();
  const results = await Promise.all(Array.from({ length: N }, () => new Promise(res => {
    const c = fork(__filename, ['--child'], { env: { ...process.env } });
    c.on('message', m => res(m));
    c.on('exit', code => res({ exit: code }));
  })));
  const wall = Date.now() - t0;
  const after = fs.readdirSync(REG);
  console.log('all ' + N + ' finished in ' + wall + 'ms wall');
  results.forEach((r, i) => console.log('  child ' + i + ': ' + JSON.stringify(r)));
  console.log('count AFTER : ' + after.length);
  console.log('REAPED count: ' + (before - after.length));
  console.log('LIVE descriptor survived ' + N + ' concurrent reaps: ' + after.includes(liveGuid));
  console.log('survivors: ' + JSON.stringify(after));

  // Is the live browser still usable after being probe-connected N times?
  const page = context.pages()[0] || await context.newPage();
  await page.goto('data:text/html,<h1 id=x>still-here</h1>');
  console.log('live browser after ' + N + ' concurrent probes: ' + JSON.stringify(await page.textContent('#x'))
    + ' isConnected=' + browser.isConnected());

  await context.close();
  await browser.close();
  console.log('\nREAL directory at start: ' + realStart + '   at end: ' + fs.readdirSync(REAL).length);
})().then(() => process.exit(0), e => { console.error('FATAL', e); process.exit(1); });
