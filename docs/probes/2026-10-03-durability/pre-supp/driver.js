// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Scratch rig (durability research, 2026-10-03), grown from .work\hard-kill\rig\driver3.js.
// One Playwright driver, run under the payload's node and playwright-core (@playwright/mcp 0.0.83,
// playwright-core 1.64.0-alpha-1790635538000), launching the way @playwright/mcp launches for the
// config BrowserAI generates on origin/next 5f1166c.
// Protocol on stdio, one line each: READY, then waits for GO; LAUNCHED or LAUNCH-FAILED;
//   write mode: WRITTEN, then accepts FLUSH <kind>, WORK <secs> <flushEveryMs>, CLOSE;
//   read mode: READ, then CLOSED and exits.
'use strict';
const readline = require('node:readline');
const http = require('node:http');

const pw = require(process.env.HK_PW);
const a = JSON.parse(process.argv[2]);

function out(tag, obj) { process.stdout.write(tag + ' ' + JSON.stringify(obj) + '\n'); }
const sleep = ms => new Promise(r => setTimeout(r, ms));

const rl = readline.createInterface({ input: process.stdin });
const queue = [];
let waiter = null;
rl.on('line', l => { if (waiter) { const w = waiter; waiter = null; w(l); } else queue.push(l); });
function nextLine() { return new Promise(res => { if (queue.length) res(queue.shift()); else waiter = res; }); }

function getJson(url) {
  return new Promise((resolve, reject) => {
    http.get(url, res => { let b = ''; res.setEncoding('utf8'); res.on('data', d => { b += d; }); res.on('end', () => { try { resolve(JSON.parse(b)); } catch (e) { reject(e); } }); }).on('error', reject);
  });
}

// BrowserConfiguration.ChromiumArguments on origin/next 5f1166c, in order.
const BROWSERAI_CHROMIUM_ARGS = ['--enable-automation', '--disable-blink-features=AutomationControlled', '--restore-last-session', '--enable-aggressive-domstorage-flushing'];
// FirefoxProfile and FirefoxSessionRestorePreferences on origin/next 5f1166c.
const BROWSERAI_FIREFOX_PREFS = {
  'toolkit.winRegisterApplicationRestart': false,
  'signon.rememberSignons': false,
  'browser.sessionstore.resume_session_once': true,
  'browser.sessionstore.restore_on_demand': false,
  'browser.sessionstore.restore_tabs_lazily': false,
};

function asArray(x) { return Array.isArray(x) ? x : (x ? [x] : []); }

function launchOptions() {
  // createPersistentBrowser in coreBundle.js (0.0.83): {...launchOptions, ...contextOptions,
  // handleSIGINT:false, handleSIGTERM:false, ignoreDefaultArgs:['--disable-extensions', ...config]}.
  // chromiumSandbox:true comes from the --sandbox flag BrowserAI passes (ChildLaunch.SandboxFlag).
  const o = {
    headless: true,
    downloadsPath: a.downloads,
    viewport: { width: 1920, height: 1080 },
    locale: 'nl-NL',
    timezoneId: 'Europe/Berlin',
    ignoreHTTPSErrors: false,
    handleSIGINT: false,
    handleSIGTERM: false,
    ignoreDefaultArgs: ['--disable-extensions', 'about:blank'],
  };
  if (a.browser === 'chromium') {
    const drop = new Set(asArray(a.dropArgs));
    o.channel = 'chrome-for-testing';
    o.args = [...BROWSERAI_CHROMIUM_ARGS.filter(x => !drop.has(x)), ...asArray(a.extraArgs)];
    o.chromiumSandbox = true;
    o.permissions = ['clipboard-read'];
  } else {
    o.firefoxUserPrefs = { ...BROWSERAI_FIREFOX_PREFS, ...(a.extraPrefs || {}) };
  }
  return o;
}

async function doFlush(ctx, kind) {
  const t0 = Date.now();
  let result;
  if (kind === 'cdp-clearorigin') {
    const page = ctx.pages()[0];
    const s = await ctx.newCDPSession(page);
    result = await s.send('Storage.clearDataForOrigin', { origin: 'https://durability-flush.invalid', storageTypes: 'cookies' });
    await s.detach();
  } else if (kind === 'cdp-setdel') {
    const page = ctx.pages()[0];
    const s = await ctx.newCDPSession(page);
    await s.send('Network.setCookie', { name: 'hk_flush', value: '1', domain: 'durability-flush.invalid', path: '/', expires: Math.floor(Date.now() / 1000) + 60 });
    result = await s.send('Network.deleteCookies', { name: 'hk_flush', domain: 'durability-flush.invalid', path: '/' });
    await s.detach();
  } else if (kind === 'batch512') {
    // The cookie store commits at once when a batch reaches 512 operations
    // (sqlite_persistent_cookie_store.cc:1237, 1288). 512 throwaway cookies on a domain no site
    // can use, through Playwright's own addCookies, cross that line from any starting count.
    const exp = Math.floor(Date.now() / 1000) + 60;
    await ctx.addCookies(Array.from({ length: 512 }, (_, i) => ({ name: 'hk_b' + i, value: '1', domain: 'durability-flush.invalid', path: '/', expires: exp })));
    result = { added: 512 };
  } else if (kind === 'pageclose') {
    // Keep the browser alive on a blank page, then close every page of the test origin.
    const keep = await ctx.newPage();
    for (const p of ctx.pages()) if (p !== keep) await p.close();
    result = { open: ctx.pages().map(p => p.url()) };
  } else {
    throw new Error('unknown flush kind ' + kind);
  }
  return { kind, t0, t: Date.now(), ms: Date.now() - t0, result };
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
  out('LAUNCHED', { ms: Date.now() - tL0, version: ctx.browser() ? ctx.browser().version() : null, pages: ctx.pages().map(p => p.url()) });
  const base = `http://127.0.0.1:${a.port}`;

  if (a.mode === 'warm') {
    // A first session in the profile, closed cleanly, so the measured launch is a resumed
    // profile and not a new one (Chromium ignores --restore-last-session for a new profile).
    for (;;) {
      const cmd = await nextLine();
      if (cmd === 'CLOSE') { const t = Date.now(); await ctx.close(); out('CLOSED', { ms: Date.now() - t }); process.exit(0); }
    }
  }

  if (a.mode === 'write' || a.mode === 'work') {
    const marks = {};
    const first = ctx.pages()[0] || await ctx.newPage();
    if (a.preIdleMs) { await sleep(a.preIdleMs); marks.preIdleMs = a.preIdleMs; }
    if (a.syncToSave && a.browser === 'firefox') {
      // Make a session change, then wait until Firefox has written it: the measured writes
      // then start right after a session-store save, which is the worst case for its interval.
      const fsm = require('node:fs'), pth = require('node:path');
      const rec = pth.join(a.profile, 'sessionstore-backups', 'recovery.jsonlz4');
      const st = () => { try { const s = fsm.statSync(rec); return s.mtimeMs + ':' + s.size; } catch { return 'absent'; } };
      const before = st();
      const tPrime = Date.now();
      await first.goto(`${base}/prime?run=${encodeURIComponent(a.run)}`);
      while (st() === before && Date.now() - tPrime < 40000) await sleep(25);
      marks.primeToSaveMs = Date.now() - tPrime;
      marks.saveSeen = st() !== before;
    }
    if (a.mode === 'write') {
      if (a.prefsChange && a.browser === 'chromium') {
        const sp = await ctx.newPage();
        await sp.goto('chrome://settings/appearance');
        marks.prefSet = await sp.evaluate(() => new Promise(res => chrome.settingsPrivate.setPref('bookmark_bar.show_on_all_tabs', true, '', ok => res(ok))));
        marks.tPref = Date.now();
        await sp.close();
      }
      for (let k = 1; k <= (a.tabs || 0); k++) {
        const tp = await ctx.newPage();
        await tp.goto(`${base}/tab?run=${encodeURIComponent(a.run)}&k=${k}`);
        await tp.waitForFunction(() => document.title.startsWith('tab-'), null, { timeout: 30000 });
      }
      marks.tTabs = Date.now();
      await first.bringToFront();
      await first.goto(`${base}/write?run=${encodeURIComponent(a.run)}&tag=${a.tag || 'a'}`);
      const done = await (await first.waitForFunction(() => window.__done, null, { timeout: 60000 })).jsonValue();
      out('WRITTEN', { ...done, ...marks, pages: ctx.pages().map(p => p.url()) });
    } else {
      out('WRITTEN', { work: true, tDone: Date.now(), marks });
    }
    for (;;) {
      const cmd = await nextLine();
      if (cmd === 'CLOSE') {
        const t = Date.now();
        await ctx.close();
        out('CLOSED', { ms: Date.now() - t });
        process.exit(0);
      } else if (cmd.startsWith('FLUSH ')) {
        try { out('FLUSHED', await doFlush(ctx, cmd.slice(6).trim())); } catch (e) { out('ERR', { msg: 'flush: ' + String(e && e.message || e) }); }
      } else if (cmd.startsWith('WRITE ')) {
        const d = await first.evaluate(t => window.__hkWrite(t), cmd.slice(6).trim());
        out('WRITTEN', d);
      } else if (cmd.startsWith('WORK ')) {
        // WORK <secs> <flushKind|none> <flushEveryMs>
        const [secs, flushKind, everyMs] = cmd.slice(5).trim().split(/\s+/);
        const wp = first;
        const t0 = Date.now();
        await wp.goto(`${base}/work?run=${encodeURIComponent(a.run)}&secs=${secs}`);
        let flushes = 0, flushMs = 0, stop = false;
        const flusher = (async () => {
          if (!flushKind || flushKind === 'none') return;
          const s = await ctx.newCDPSession(wp);
          while (!stop) {
            await sleep(Number(everyMs));
            if (stop) break;
            const f0 = Date.now();
            if (flushKind === 'cdp-clearorigin') await s.send('Storage.clearDataForOrigin', { origin: 'https://durability-flush.invalid', storageTypes: 'cookies' });
            flushMs += Date.now() - f0; flushes++;
          }
        })();
        const w = await (await wp.waitForFunction(() => window.__work, null, { timeout: (Number(secs) + 60) * 1000, polling: 500 })).jsonValue();
        stop = true;
        await flusher.catch(() => {});
        out('WORKED', { ...w, flushes, flushMs, wallMs: Date.now() - t0 });
      }
    }
  } else {
    // read and mid modes: let the browser's own session restore settle first.
    const phase = a.phase || 'read';
    const t0 = Date.now();
    const settleMs = a.settleMs || 8000;
    let lastSig = '', stableSince = Date.now(), seen = {};
    while (Date.now() - t0 < settleMs) {
      const urls = ctx.pages().map(p => p.url());
      const sig = urls.join('|');
      if (sig !== lastSig) { lastSig = sig; stableSince = Date.now(); }
      try { const st = await getJson(`${base}/status?run=${encodeURIComponent(a.run)}`); seen = (st.tabs && st.tabs[phase]) || {}; } catch { }
      const want = a.expectTabs || 0;
      if (want > 0 && Object.keys(seen).length >= want && Date.now() - stableSince >= 1000) break;
      await sleep(250);
    }
    const urlsAfterSettle = ctx.pages().map(p => p.url());
    if (a.mode === 'mid') {
      // The session after a hard kill: does a tab opened now reach the session file?
      const tp = await ctx.newPage();
      await tp.goto(`${base}/tab?run=${encodeURIComponent(a.run)}&k=${a.midTab || 9}`);
      await tp.waitForFunction(() => document.title.startsWith('tab-'), null, { timeout: 30000 });
      out('MID', { settleMs: Date.now() - t0, urlsAfterSettle, tabsSeen: seen, tMidTab: Date.now(), pages: ctx.pages().map(p => p.url()) });
      for (;;) {
        const cmd = await nextLine();
        if (cmd === 'CLOSE') { const t = Date.now(); await ctx.close(); out('CLOSED', { ms: Date.now() - t }); process.exit(0); }
      }
    }
    const rp = await ctx.newPage();
    await rp.goto(`${base}/read?run=${encodeURIComponent(a.run)}`);
    const read = await (await rp.waitForFunction(() => window.__read, null, { timeout: 60000 })).jsonValue();
    const cookies = (await ctx.cookies()).map(c => ({ name: c.name, value: c.value, expires: c.expires, httpOnly: c.httpOnly, domain: c.domain }));
    out('READ', { settleMs: Date.now() - t0, urlsAfterSettle, tabsSeen: seen, read, cookies });
    const t = Date.now();
    await ctx.close();
    out('CLOSED', { ms: Date.now() - t });
    process.exit(0);
  }
})().catch(e => { out('ERR', { msg: String((e && e.stack) || e).slice(0, 4000) }); process.exit(1); });
