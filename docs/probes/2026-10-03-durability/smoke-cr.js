// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Scratch smoke test (durability research, 2026-10-03): can chrome://settings set a profile pref
// from Playwright, and does a CDP Storage.clearDataForOrigin on an unused origin make the cookie
// store commit? Watches the files' size and mtime. Clean close at the end.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const pw = require(process.env.HK_PW);
const profile = process.argv[2];

function stat(f) { try { const s = fs.statSync(f); return s.size + '@' + s.mtimeMs.toFixed(0); } catch { return 'absent'; } }
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const srv = http.createServer((req, res) => {
    res.writeHead(200, { 'content-type': 'text/html', 'set-cookie': 'smoke=1; Max-Age=86400; Path=/' });
    res.end('<!doctype html><title>smoke</title>ok');
  });
  await new Promise(r => srv.listen(0, '127.0.0.1', r));
  const port = srv.address().port;
  const ctx = await pw.chromium.launchPersistentContext(profile, {
    headless: true, channel: 'chrome-for-testing', chromiumSandbox: true, handleSIGINT: false, handleSIGTERM: false,
    args: ['--enable-automation', '--disable-blink-features=AutomationControlled', '--restore-last-session', '--enable-aggressive-domstorage-flushing'],
    ignoreDefaultArgs: ['--disable-extensions', 'about:blank'],
    viewport: { width: 1920, height: 1080 }, locale: 'nl-NL', timezoneId: 'Europe/Berlin', ignoreHTTPSErrors: false, permissions: ['clipboard-read'],
  });
  const out = { version: ctx.browser().version() };
  const cookies = path.join(profile, 'Default', 'Network', 'Cookies');
  const prefs = path.join(profile, 'Default', 'Preferences');
  const page = ctx.pages()[0] || await ctx.newPage();
  out.firstUrl = page.url();
  await sleep(12000); // let startup writes settle (Preferences commit interval is 10 s)
  out.cookiesBeforeNav = stat(cookies);
  await page.goto(`http://127.0.0.1:${port}/`);
  const tNav = Date.now();
  const series = [];
  for (let i = 0; i < 10; i++) { series.push([Date.now() - tNav, stat(cookies)]); await sleep(100); }
  out.cookiesAfterNav1s = series;
  const b = await ctx.browser().newBrowserCDPSession();
  const tF = Date.now();
  let res;
  try { res = await b.send('Storage.clearDataForOrigin', { origin: 'https://durability-flush.invalid', storageTypes: 'cookies' }); } catch (e) { res = 'ERR ' + e.message; }
  out.flushCallMs = Date.now() - tF;
  out.flushResult = res;
  const s2 = [];
  for (let i = 0; i < 15; i++) { s2.push([Date.now() - tF, stat(cookies)]); await sleep(100); }
  out.cookiesAfterFlush = s2;
  out.cookiesList = (await ctx.cookies()).map(c => c.name + '=' + c.value + '@' + c.domain);
  // Preferences through chrome://settings
  const sp = await ctx.newPage();
  try {
    await sp.goto('chrome://settings/appearance');
    out.settingsUrl = sp.url();
    out.hasSettingsPrivate = await sp.evaluate(() => typeof chrome !== 'undefined' && !!chrome.settingsPrivate);
    out.prefsBefore = stat(prefs);
    const tP = Date.now();
    out.setPref = await sp.evaluate(() => new Promise(res => {
      try {
        const r = chrome.settingsPrivate.setPref('bookmark_bar.show_on_all_tabs', true, '', ok => res({ cb: ok, err: chrome.runtime && chrome.runtime.lastError ? String(chrome.runtime.lastError.message) : null }));
        if (r && typeof r.then === 'function') r.then(v => res({ promise: v }), e => res({ promiseErr: String(e) }));
      } catch (e) { res({ threw: String(e) }); }
    }));
    out.getPref = await sp.evaluate(() => new Promise(res => chrome.settingsPrivate.getPref('bookmark_bar.show_on_all_tabs', p => res(p && p.value))));
    const s3 = [];
    for (let i = 0; i < 26; i++) { s3.push([Date.now() - tP, stat(prefs)]); await sleep(500); }
    out.prefsAfterSet = s3;
    const j = JSON.parse(fs.readFileSync(prefs, 'utf8'));
    out.prefOnDisk = j.bookmark_bar;
    out.exitTypeOnDisk = j.profile && j.profile.exit_type;
  } catch (e) { out.settingsError = String(e && e.message || e); }
  out.sessionsDir = fs.existsSync(path.join(profile, 'Default', 'Sessions')) ? fs.readdirSync(path.join(profile, 'Default', 'Sessions')).map(f => f + ':' + fs.statSync(path.join(profile, 'Default', 'Sessions', f)).size) : 'absent';
  await ctx.close();
  srv.close();
  console.log(JSON.stringify(out, null, 1));
})().catch(e => { console.log('ERR ' + (e && e.stack || e)); process.exit(1); });
