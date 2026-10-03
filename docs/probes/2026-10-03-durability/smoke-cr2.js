// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Scratch smoke test 2 (durability research, 2026-10-03): which CDP call on a PAGE session makes
// Chromium's cookie store commit at once? Tries Storage.clearDataForOrigin on an unused origin,
// then Network.setCookie + Network.deleteCookies on an unused domain, each after a fresh cookie
// write, watching the Cookies file. Clean close at the end.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const pw = require(process.env.HK_PW);
const profile = process.argv[2];

function stat(f) { try { const s = fs.statSync(f); return s.size + '@' + s.mtimeMs.toFixed(0); } catch { return 'absent'; } }
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function watch(f, ms) {
  const t0 = Date.now(), first = stat(f);
  while (Date.now() - t0 < ms) { const s = stat(f); if (s !== first) return { changedAfterMs: Date.now() - t0, from: first, to: s }; await sleep(20); }
  return { changedAfterMs: null, at: first };
}

(async () => {
  let n = 0;
  const srv = http.createServer((req, res) => {
    n++;
    res.writeHead(200, { 'content-type': 'text/html', 'set-cookie': `smoke${n}=1; Max-Age=86400; Path=/` });
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
  const out = {};
  const cookies = path.join(profile, 'Default', 'Network', 'Cookies');
  const page = ctx.pages()[0] || await ctx.newPage();
  await page.goto(`http://127.0.0.1:${port}/`);
  out.firstCookieThenIdle = await watch(cookies, 1500);
  const s = await ctx.newCDPSession(page);
  // A: Storage.clearDataForOrigin on an unused origin, page session.
  await page.goto(`http://127.0.0.1:${port}/a`);
  let t = Date.now();
  try { out.A_result = await s.send('Storage.clearDataForOrigin', { origin: 'https://durability-flush.invalid', storageTypes: 'cookies' }); } catch (e) { out.A_result = 'ERR ' + e.message; }
  out.A_callMs = Date.now() - t;
  out.A_watch = await watch(cookies, 2000);
  // B: set + delete a throwaway cookie on an unused domain, page session.
  await page.goto(`http://127.0.0.1:${port}/b`);
  out.B_beforeIdle = await watch(cookies, 500);
  t = Date.now();
  try {
    out.B_set = await s.send('Network.setCookie', { name: 'hk_flush', value: '1', domain: 'durability-flush.invalid', path: '/', expires: Math.floor(Date.now() / 1000) + 60 });
    out.B_del = await s.send('Network.deleteCookies', { name: 'hk_flush', domain: 'durability-flush.invalid', path: '/' });
  } catch (e) { out.B_err = 'ERR ' + e.message; }
  out.B_callMs = Date.now() - t;
  out.B_watch = await watch(cookies, 2000);
  // C: Network.deleteCookies matching nothing at all (no set first).
  await page.goto(`http://127.0.0.1:${port}/c`);
  t = Date.now();
  try { out.C_del = await s.send('Network.deleteCookies', { name: 'hk_none', domain: 'durability-flush.invalid' }); } catch (e) { out.C_err = 'ERR ' + e.message; }
  out.C_callMs = Date.now() - t;
  out.C_watch = await watch(cookies, 2000);
  // D: Storage.clearDataForOrigin through the browser session, for the record.
  const b = await ctx.browser().newBrowserCDPSession();
  try { out.D_result = await b.send('Storage.clearDataForOrigin', { origin: 'https://durability-flush.invalid', storageTypes: 'cookies' }); } catch (e) { out.D_result = 'ERR ' + e.message; }
  out.cookiesList = (await ctx.cookies()).map(c => c.name + '@' + c.domain).join(' ');
  await ctx.close();
  srv.close();
  console.log(JSON.stringify(out, null, 1));
})().catch(e => { console.log('ERR ' + (e && e.stack || e)); process.exit(1); });
