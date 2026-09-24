// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Opens Playwright's own dashboard against a SCRATCH registry, so that:
//   * no descriptor in %LOCALAPPDATA%\ms-playwright\b is deleted, and
//   * the dashboard never connects to a browser this script did not launch.
// Seeds the scratch registry with COPIES of real stale descriptors plus one live
// browser launched here, then starts the dashboard as a child on an HTTP port.
// Stops when .work/t7-2026-09-23/STOP-DASHBOARD appears.
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const REPO = 'C:/Source/SixFive7/BrowserAI';
const SCRATCH = path.join(REPO, '.work', 't7-2026-09-23');
const PWLIB = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const pw = require(PWLIB);

const REAL = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'b');
const REG = process.env.PWTEST_SERVER_REGISTRY;
const STOP = path.join(SCRATCH, 'STOP-DASHBOARD');
const SAMPLE = 60;

const log = (...a) => { console.log(...a); };

(async () => {
  try { fs.unlinkSync(STOP); } catch {}

  // 1. Seed the scratch registry with copies of real STALE descriptors only.
  fs.rmSync(REG, { recursive: true, force: true });
  fs.mkdirSync(REG, { recursive: true });
  const realAll = fs.readdirSync(REAL);
  const sample = realAll.slice(0, SAMPLE);
  for (const n of sample) fs.copyFileSync(path.join(REAL, n), path.join(REG, n));
  log('real registry holds        : ' + realAll.length + ' descriptors (untouched by this script)');
  log('scratch registry seeded    : ' + fs.readdirSync(REG).length + ' copies of stale descriptors');

  // 2. One live browser, launched here, bound into the scratch registry.
  const profile = path.join(SCRATCH, 'profile-dash');
  fs.mkdirSync(profile, { recursive: true });
  const context = await pw.chromium.launchPersistentContext(profile, {
    headless: true, handleSIGINT: false, handleSIGTERM: false,
  });
  const browser = context.browser();
  const bound = await browser.bind('t7-dashboard-demo', { workspaceDir: SCRATCH });
  const page = context.pages()[0] || await context.newPage();
  await page.goto('data:text/html,<title>BrowserAI descriptor demo</title><h1>a live bound browser</h1>');
  const p2 = await context.newPage();
  await p2.goto('data:text/html,<title>second tab</title><p>second tab</p>');
  log('live browser bound         : guid=' + browser._guid);
  log('   endpoint                : ' + bound.endpoint);
  log('   tabs                    : ' + context.pages().length);
  log('scratch registry now       : ' + fs.readdirSync(REG).length + ' (60 stale + 1 live)');

  // 3. The dashboard, as a child, on an HTTP port so it serves instead of
  //    opening a desktop window. stdin is kept as a live pipe: the dashboard
  //    self-destructs when its stdin closes (coreBundle.js:77156).
  const entry = path.join(PWLIB, 'lib', 'entry', 'dashboardApp.js');
  const child = spawn(process.execPath, [entry, '--port=0', '--host=127.0.0.1', '--workspaceDir=' + SCRATCH], {
    env: { ...process.env },
    stdio: ['pipe', 'pipe', 'pipe'],
  });
  log('dashboard child pid        : ' + child.pid);
  log('dashboard entry            : ' + entry);

  let url = null;
  child.stdout.on('data', d => {
    const s = String(d);
    process.stdout.write('[dashboard stdout] ' + s);
    const m = s.match(/Listening on (\S+)/);
    if (m) {
      url = m[1];
      fs.writeFileSync(path.join(SCRATCH, 'dashboard-url.txt'), url, 'utf-8');
      log('DASHBOARD URL              : ' + url);
    }
  });
  child.stderr.on('data', d => process.stdout.write('[dashboard stderr] ' + String(d)));
  child.on('exit', (code, sig) => log('dashboard child exited code=' + code + ' signal=' + sig));

  // 4. Report what the reap did to the scratch copies once the dashboard has
  //    listed (its provider calls serverRegistry.list(), which unlinks).
  const report = () => {
    const left = fs.readdirSync(REG);
    log('scratch registry after list: ' + left.length + ' -> ' + JSON.stringify(left));
    log('real registry still        : ' + fs.readdirSync(REAL).length);
  };
  setTimeout(report, 8000);
  setTimeout(report, 20000);

  // 5. Stay up until told to stop.
  log('STOP FILE                  : ' + STOP);
  const started = Date.now();
  while (!fs.existsSync(STOP) && Date.now() - started < 3 * 3600 * 1000) {
    await new Promise(r => setTimeout(r, 2000));
  }
  log('stopping: stop file seen or 3h elapsed');
  try { child.stdin.end(); } catch {}
  try { child.kill(); } catch {}
  await context.close().catch(() => {});
  await browser.close().catch(() => {});
  log('final scratch registry     : ' + fs.readdirSync(REG).length);
  log('final real registry        : ' + fs.readdirSync(REAL).length);
  process.exit(0);
})().catch(e => { console.error('FATAL', e); process.exit(1); });
