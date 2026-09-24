// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Phase A: descriptor lifecycle against the REAL registry directory.
// Launches chromium through the repository's own payload playwright-core with a
// persistent profile under this scratch directory. Deletes ONLY the descriptors
// this launch created. Kills only the browser this script launched.
const fs = require('fs');
const path = require('path');

const REPO = 'C:/Source/SixFive7/BrowserAI';
const SCRATCH = path.join(REPO, '.work', 't7-2026-09-23');
const PWLIB = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const pw = require(PWLIB);
const REG = process.env.PWTEST_SERVER_REGISTRY || path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'b');

const log = (...a) => console.log(...a);
const snap = () => new Set(fs.existsSync(REG) ? fs.readdirSync(REG) : []);
const diff = (before, after) => [...after].filter(n => !before.has(n));

async function run(label, profileName, deleteWhileRunning) {
  log('\n################ ' + label + ' ################');
  const profile = path.join(SCRATCH, profileName);
  fs.mkdirSync(profile, { recursive: true });

  const before = snap();
  log('[' + label + '] registry dir          : ' + REG);
  log('[' + label + '] count BEFORE launch   : ' + before.size);

  const context = await pw.chromium.launchPersistentContext(profile, {
    headless: true,
    handleSIGINT: false,
    handleSIGTERM: false,
    downloadsPath: path.join(SCRATCH, 'downloads'),
    tracesDir: path.join(SCRATCH, 'traces'),
  });
  const browser = context.browser();
  log('[' + label + '] launched. browser guid: ' + browser._guid);
  log('[' + label + '] browser version       : ' + browser.version());

  const afterLaunchNoBind = snap();
  log('[' + label + '] count after launch, BEFORE bind: ' + afterLaunchNoBind.size
    + '  (new: ' + JSON.stringify(diff(before, afterLaunchNoBind)) + ')');

  const bound = await browser.bind('t7-descriptor-probe-' + label, { workspaceDir: SCRATCH });
  const afterBind = snap();
  const added = diff(before, afterBind);
  log('[' + label + '] bind() endpoint       : ' + bound.endpoint);
  log('[' + label + '] count AFTER bind      : ' + afterBind.size);
  log('[' + label + '] files ADDED by me     : ' + JSON.stringify(added));
  for (const n of added) {
    log('[' + label + '] --- content of ' + n + ' ---');
    log(fs.readFileSync(path.join(REG, n), 'utf-8'));
  }
  const mine = added.filter(n => {
    try { return JSON.parse(fs.readFileSync(path.join(REG, n), 'utf-8')).title === 't7-descriptor-probe-' + label; }
    catch { return false; }
  });
  log('[' + label + '] confirmed MINE by content: ' + JSON.stringify(mine));

  const page = context.pages()[0] || await context.newPage();
  await page.goto('data:text/html,<h1 id=x>alive-1</h1>');
  log('[' + label + '] page works before delete: innerText=' + JSON.stringify(await page.textContent('#x')));

  if (deleteWhileRunning) {
    for (const n of mine) {
      fs.unlinkSync(path.join(REG, n));
      log('[' + label + '] DELETED while running : ' + n + '  exists-now=' + fs.existsSync(path.join(REG, n)));
    }
    await new Promise(r => setTimeout(r, 1500));
    log('[' + label + '] recreated after 1.5s? : ' + JSON.stringify(mine.map(n => fs.existsSync(path.join(REG, n)))));

    try {
      await page.goto('data:text/html,<h1 id=x>alive-2</h1>');
      const t = await page.textContent('#x');
      const ev = await page.evaluate(() => 2 + 40);
      log('[' + label + '] page AFTER delete     : OK  innerText=' + JSON.stringify(t) + ' evaluate=' + ev);
    } catch (e) {
      log('[' + label + '] page AFTER delete     : FAILED ' + e.message);
    }
    try {
      const p2 = await context.newPage();
      await p2.goto('data:text/html,<p>second page</p>');
      log('[' + label + '] newPage AFTER delete  : OK  title=' + JSON.stringify(await p2.title()));
      await p2.close();
    } catch (e) {
      log('[' + label + '] newPage AFTER delete  : FAILED ' + e.message);
    }
  }

  let closeError = null;
  const t0 = Date.now();
  try { await context.close(); } catch (e) { closeError = e; }
  try { await browser.close(); } catch (e) { closeError = closeError || e; }
  const ms = Date.now() - t0;
  log('[' + label + '] close()               : ' + (closeError ? 'FAILED ' + closeError.message : 'OK') + ' in ' + ms + 'ms');
  log('[' + label + '] browser.isConnected() : ' + browser.isConnected());

  await new Promise(r => setTimeout(r, 1500));
  const afterClose = snap();
  log('[' + label + '] count AFTER close     : ' + afterClose.size);
  log('[' + label + '] my descriptor present after close: ' + JSON.stringify(mine.map(n => fs.existsSync(path.join(REG, n)))));
  log('[' + label + '] net new vs BEFORE     : ' + JSON.stringify(diff(before, afterClose)));
  return { mine, afterClose };
}

(async () => {
  // A1: delete the descriptor WHILE the browser runs.
  await run('A1', 'profile-a1', true);

  // A2: normal close (proves a persistent descriptor survives close).
  const r2 = await run('A2', 'profile-a2', false);

  log('\n################ A3: delete AFTER close ################');
  for (const n of r2.mine) {
    const p = path.join(REG, n);
    if (fs.existsSync(p)) {
      fs.unlinkSync(p);
      log('[A3] deleted after close       : ' + n + ' exists-now=' + fs.existsSync(p));
    } else {
      log('[A3] already gone after close  : ' + n);
    }
  }

  // A4: does a subsequent launch + bind still work with the old one removed?
  const r3 = await run('A4', 'profile-a4', false);
  for (const n of r3.mine) {
    const p = path.join(REG, n);
    if (fs.existsSync(p)) { fs.unlinkSync(p); log('[A4] cleaned up my descriptor  : ' + n + ' exists-now=' + fs.existsSync(p)); }
  }
  log('\nFINAL registry count: ' + snap().size);
})().then(() => process.exit(0), e => { console.error('FATAL', e); process.exit(1); });
