// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch rig: one Playwright driver, run under the payload's own node and playwright-core.
// Protocol on stdio, one line each: READY, then waits for GO; LAUNCHED / LAUNCH-FAILED;
// write mode: WRITTEN, then accepts "WRITE <tag>" (a second write in the same document)
// and "CLOSE" (a clean close, the control); read mode: READ then CLOSED and exits.
'use strict';
const readline = require('node:readline');

const pw = require(process.env.HK_PW);
const a = JSON.parse(process.argv[2]);

function out(tag, obj) { process.stdout.write(tag + ' ' + JSON.stringify(obj) + '\n'); }

const rl = readline.createInterface({ input: process.stdin });
const queue = [];
let waiter = null;
rl.on('line', l => { if (waiter) { const w = waiter; waiter = null; w(l); } else queue.push(l); });
function nextLine() { return new Promise(res => { if (queue.length) res(queue.shift()); else waiter = res; }); }

function launchOptions() {
  // What @playwright/mcp's createPersistentBrowser passes, with BrowserAI's generated config
  // (BrowserConfiguration.Generate) and --sandbox (ChildLaunch.SandboxFlag) folded in.
  const o = {
    headless: true,
    downloadsPath: a.downloads,
    viewport: { width: 1920, height: 1080 },
    locale: a.locale,
    timezoneId: a.tz,
    ignoreHTTPSErrors: false,
    handleSIGINT: false,
    handleSIGTERM: false,
    ignoreDefaultArgs: ['--disable-extensions'],
  };
  if (a.browser === 'chromium') {
    o.channel = 'chrome-for-testing';
    // A one-element array can arrive as a bare string from PowerShell's JSON writer; spreading a
    // string would pass its characters as arguments.
    const extra = Array.isArray(a.extraArgs) ? a.extraArgs : (a.extraArgs ? [a.extraArgs] : []);
    o.args = ['--enable-automation', '--disable-blink-features=AutomationControlled', ...extra];
    o.chromiumSandbox = true;
    o.permissions = ['clipboard-read'];
  } else {
    o.firefoxUserPrefs = { 'toolkit.winRegisterApplicationRestart': false, 'signon.rememberSignons': false };
  }
  return o;
}

(async () => {
  out('READY', { pid: process.pid });
  const go = await nextLine();
  if (go !== 'GO') { out('ERR', { msg: 'expected GO, got ' + go }); process.exit(2); }

  const tL0 = Date.now();
  let ctx;
  try {
    ctx = await pw[a.browser].launchPersistentContext(a.profile, launchOptions());
  } catch (e) {
    out('LAUNCH-FAILED', { ms: Date.now() - tL0, error: String((e && e.message) || e).slice(0, 4000) });
    process.exit(3);
  }
  out('LAUNCHED', { ms: Date.now() - tL0, version: ctx.browser() ? ctx.browser().version() : null });
  const page = ctx.pages()[0] || await ctx.newPage();

  if (a.mode === 'write') {
    await page.goto(`http://127.0.0.1:${a.port}/write?run=${a.run}&tag=${a.tag || 'a'}`);
    const done = await (await page.waitForFunction(() => window.__done, null, { timeout: 60000 })).jsonValue();
    out('WRITTEN', done);
    for (;;) {
      const cmd = await nextLine();
      if (cmd === 'CLOSE') {
        const t = Date.now();
        await ctx.close();
        out('CLOSED', { ms: Date.now() - t });
        process.exit(0);
      } else if (cmd.startsWith('WRITE ')) {
        const tag = cmd.slice(6).trim();
        const d = await page.evaluate(t => window.__hkWrite(t), tag);
        out('WRITTEN', d);
      }
    }
  } else {
    await page.goto(`http://127.0.0.1:${a.port}/read?run=${a.run}`);
    const read = await (await page.waitForFunction(() => window.__read, null, { timeout: 60000 })).jsonValue();
    const cookies = (await ctx.cookies()).map(c => ({ name: c.name, value: c.value, expires: c.expires, httpOnly: c.httpOnly }));
    out('READ', { read, cookies });
    const t = Date.now();
    await ctx.close();
    out('CLOSED', { ms: Date.now() - t });
    process.exit(0);
  }
})().catch(e => { out('ERR', { msg: String((e && e.stack) || e).slice(0, 4000) }); process.exit(1); });
