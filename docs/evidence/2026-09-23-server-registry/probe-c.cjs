// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Phase C: what does ServerRegistry.list() actually REAP?
// Runs entirely inside a scratch registry (PWTEST_SERVER_REGISTRY), planted with
// COPIES of real dead descriptors from the maintainer's cache plus one LIVE
// descriptor belonging to a browser this script launches. Nothing in
// %LOCALAPPDATA%\ms-playwright\b is written or deleted by this script.
const fs = require('fs');
const path = require('path');
const REPO = 'C:/Source/SixFive7/BrowserAI';
const SCRATCH = path.join(REPO, '.work', 't7-2026-09-23');
const PWLIB = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const pw = require(PWLIB);
const { serverRegistry } = require(path.join(PWLIB, 'lib', 'serverRegistry.js'));

const REAL = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'b');
const REG = process.env.PWTEST_SERVER_REGISTRY;
const BS = String.fromCharCode(92); // backslash

(async () => {
  console.log('scratch registry: ' + REG);
  const realCountAtStart = fs.readdirSync(REAL).length;
  fs.rmSync(REG, { recursive: true, force: true });
  fs.mkdirSync(REG, { recursive: true });

  // Plant 5 real dead descriptors (copied, never moved) as stand-ins for peers.
  const real = fs.readdirSync(REAL).slice(0, 5);
  for (const n of real) fs.copyFileSync(path.join(REAL, n), path.join(REG, n));
  console.log('planted ' + real.length + ' COPIES of real (dead) descriptors:');
  for (const n of real) {
    const j = JSON.parse(fs.readFileSync(path.join(REG, n), 'utf-8'));
    console.log('   ' + n + '  title=' + JSON.stringify(j.title) + '  endpoint=' + JSON.stringify(j.endpoint));
  }

  // Plant a synthetic descriptor naming a FOREIGN playwrightLib, to model a peer.
  const peer = 'browser@ffffffffffffffffffffffffffffffff';
  fs.writeFileSync(path.join(REG, peer), JSON.stringify({
    playwrightVersion: '1.64.0-alpha-1789764292000',
    playwrightLib: 'C:' + BS + 'Some' + BS + 'Other' + BS + 'Tool' + BS + 'node_modules' + BS + 'playwright-core',
    title: 'SomeOtherTool',
    browser: { guid: peer, browserName: 'chromium', launchOptions: {}, userDataDir: 'C:' + BS + 'nope' },
    endpoint: BS + BS + '.' + BS + 'pipe' + BS + 'pw-deadbeef-browser-browser@ffffff',
    workspaceDir: 'C:' + BS + 'somewhere' + BS + 'else',
  }, null, 2), 'utf-8');
  console.log('planted 1 synthetic PEER descriptor: ' + peer);

  // Now a genuinely LIVE one.
  const profile = path.join(SCRATCH, 'profile-c');
  fs.mkdirSync(profile, { recursive: true });
  const context = await pw.chromium.launchPersistentContext(profile, {
    headless: true, handleSIGINT: false, handleSIGTERM: false,
  });
  const browser = context.browser();
  await browser.bind('t7-live-descriptor', { workspaceDir: SCRATCH });
  console.log('LIVE descriptor guid: ' + browser._guid);

  const before = fs.readdirSync(REG);
  console.log('\ncount BEFORE list(): ' + before.length + ' -> ' + JSON.stringify(before));

  const t0 = Date.now();
  const result = await serverRegistry.list();
  const ms = Date.now() - t0;
  const after = fs.readdirSync(REG);
  console.log('list() took ' + ms + 'ms and returned ' + [...result.values()].flat().length + ' connectable descriptor(s)');
  for (const [ws, list] of result) console.log('   workspace=' + JSON.stringify(ws) + ' -> ' + JSON.stringify(list.map(d => d.title)));
  console.log('count AFTER  list(): ' + after.length + ' -> ' + JSON.stringify(after));
  console.log('REAPED  : ' + JSON.stringify(before.filter(n => !after.includes(n))));
  console.log('SURVIVED: ' + JSON.stringify(after));
  console.log('live descriptor survived: ' + after.includes(browser._guid));

  // Does the page still work after the reap?
  const page = context.pages()[0] || await context.newPage();
  await page.goto('data:text/html,<h1 id=x>after-reap</h1>');
  console.log('page after reap: ' + JSON.stringify(await page.textContent('#x')));
  await context.close();
  await browser.close();
  await new Promise(r => setTimeout(r, 1200));
  console.log('count after close: ' + fs.readdirSync(REG).length + ' -> ' + JSON.stringify(fs.readdirSync(REG)));

  console.log('\nREAL directory at start: ' + realCountAtStart + '   at end: ' + fs.readdirSync(REAL).length);
})().then(() => process.exit(0), e => { console.error('FATAL', e); process.exit(1); });
