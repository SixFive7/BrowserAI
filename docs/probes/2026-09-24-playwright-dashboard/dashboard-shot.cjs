// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Screenshots the running dashboard with a headless browser launched here.
// Read-only: navigates and photographs. Clicks nothing, closes nothing,
// attaches to nothing.
const fs = require('fs');
const path = require('path');
const REPO = 'C:/Source/SixFive7/BrowserAI';
const SCRATCH = path.join(REPO, '.work', 't7-2026-09-23');
const PWLIB = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const pw = require(PWLIB);

// Deliberately NOT the scratch registry: this browser must not appear in the
// dashboard it is photographing, so it binds nothing and writes no descriptor.
delete process.env.PWTEST_SERVER_REGISTRY;

const url = fs.readFileSync(path.join(SCRATCH, 'dashboard-url.txt'), 'utf-8').trim();

(async () => {
  console.log('shooting ' + url);
  const profile = path.join(SCRATCH, 'profile-shot');
  fs.mkdirSync(profile, { recursive: true });
  const context = await pw.chromium.launchPersistentContext(profile, {
    headless: true, handleSIGINT: false, handleSIGTERM: false,
    viewport: { width: 1600, height: 1000 },
  });
  const page = context.pages()[0] || await context.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  console.log('landed on: ' + page.url());
  console.log('title    : ' + JSON.stringify(await page.title()));
  // Let the WebSocket connect and the session list arrive.
  await page.waitForTimeout(9000);

  const shot = path.join(SCRATCH, 'dashboard-after-reload.png');
  await page.screenshot({ path: shot, fullPage: true });
  console.log('screenshot: ' + shot + '  (' + fs.statSync(shot).size + ' bytes)');

  // What is on the page, as text, so the listing is readable without the image.
  const text = await page.evaluate(() => document.body.innerText);
  console.log('--- page text ---');
  console.log(text);
  console.log('--- end page text ---');
  if (errors.length) console.log('console errors: ' + JSON.stringify(errors.slice(0, 5)));

  await context.close();
})().then(() => process.exit(0), e => { console.error('FATAL', e); process.exit(1); });
