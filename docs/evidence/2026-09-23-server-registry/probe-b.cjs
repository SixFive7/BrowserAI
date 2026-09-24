// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Phase B: does PWTEST_SERVER_REGISTRY move the descriptor directory?
// Run with PWTEST_SERVER_REGISTRY pointed at scratch. Asserts the real
// %LOCALAPPDATA%\ms-playwright\b count does NOT move.
const fs = require('fs');
const path = require('path');
const REPO = 'C:/Source/SixFive7/BrowserAI';
const SCRATCH = path.join(REPO, '.work', 't7-2026-09-23');
const pw = require(path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core'));

const REAL = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'b');
const OVERRIDE = process.env.PWTEST_SERVER_REGISTRY;
const cnt = d => (fs.existsSync(d) ? fs.readdirSync(d).length : -1);

(async () => {
  console.log('PWTEST_SERVER_REGISTRY = ' + OVERRIDE);
  console.log('real    BEFORE: ' + cnt(REAL));
  console.log('scratch BEFORE: ' + cnt(OVERRIDE));
  const profile = path.join(SCRATCH, 'profile-b');
  fs.mkdirSync(profile, { recursive: true });
  const context = await pw.chromium.launchPersistentContext(profile, {
    headless: true, handleSIGINT: false, handleSIGTERM: false,
  });
  const browser = context.browser();
  const bound = await browser.bind('t7-override-probe', { workspaceDir: SCRATCH });
  console.log('guid=' + browser._guid + ' endpoint=' + bound.endpoint);
  console.log('real    AFTER bind: ' + cnt(REAL));
  console.log('scratch AFTER bind: ' + cnt(OVERRIDE)
    + '  files=' + JSON.stringify(fs.existsSync(OVERRIDE) ? fs.readdirSync(OVERRIDE) : []));
  const p = path.join(OVERRIDE, browser._guid);
  console.log('descriptor at OVERRIDE path exists: ' + fs.existsSync(p));
  if (fs.existsSync(p)) console.log(fs.readFileSync(p, 'utf-8'));
  console.log('descriptor at REAL path exists    : ' + fs.existsSync(path.join(REAL, browser._guid)));
  await context.close();
  await browser.close();
  await new Promise(r => setTimeout(r, 1200));
  console.log('real    AFTER close: ' + cnt(REAL));
  console.log('scratch AFTER close: ' + cnt(OVERRIDE) + '  (persistent profile => descriptor should SURVIVE)');
  console.log('my descriptor still in scratch: ' + fs.existsSync(p));
})().then(() => process.exit(0), e => { console.error('FATAL', e); process.exit(1); });
