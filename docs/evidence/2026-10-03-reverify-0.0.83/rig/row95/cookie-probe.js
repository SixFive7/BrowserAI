// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Re-establishes re-verification row 95 on 2026-10-03: a session BrowserAI
// configures, headless, navigated twice to a loopback server that sets a
// persistent cookie, the cookie confirmed through document.cookie, then
// browser_close so the store is flushed. A SEPARATE process (read-store.js)
// then reads Local State and a COPY of the Cookies database and decrypts.
// The session lives under this lane's scratch and is destroyed at the end.
'use strict';
const http = require('node:http');
const path = require('node:path');
const fs = require('node:fs');
const { execFileSync } = require('node:child_process');
const { Mcp, text } = require('./mcp.js');

const EXE = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\wt\\stale\\src\\BrowserAI\\bin\\Release\\net10.0-windows\\win-x64\\publish\\BrowserAI.Server.exe';
const OUT = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch\\out\\row95';
const SESSION = path.join(OUT, 'session');
const VALUE = 'row95-' + Date.now().toString(36) + '-persistent-cookie-value';

fs.rmSync(OUT, { recursive: true, force: true });
fs.mkdirSync(OUT, { recursive: true });
const report = { utc: new Date().toISOString(), exe: EXE, session: SESSION, value: VALUE };
const say = (k, v) => { report[k] = v; console.log(k, '=', typeof v === 'string' ? v.slice(0, 400) : JSON.stringify(v).slice(0, 400)); };

(async () => {
  const server = http.createServer((req, res) => {
    res.writeHead(200, {
      'content-type': 'text/html; charset=utf-8',
      'cache-control': 'no-store',
      'set-cookie': `browserai_probe=${VALUE}; Max-Age=86400; Path=/`,
    });
    res.end('<!doctype html><meta charset=utf-8><title>row95</title><p>row95</p>');
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const url = 'http://127.0.0.1:' + server.address().port + '/';
  say('origin', url);

  const m = new Mcp(EXE);
  await m.initialize();
  const init = await m.tool('browserai_init', { directory: SESSION, browser: 'chromium', purpose: 'Re-verification row 95 on 2026-10-03: whether a session cookie store decrypts with DPAPI alone at chromium 1247.' });
  say('init', text(init).slice(0, 300));
  for (const n of [1, 2]) {
    const nav = await m.tool('browser_navigate', { session: SESSION, why: 'Load the loopback page that sets the persistent cookie this row decrypts.', url });
    say('navigate' + n + 'Ms', nav.ms);
  }
  say('documentCookie', text(await m.tool('browser_evaluate', { session: SESSION, why: 'Confirm the cookie is live before the store is flushed.', function: '() => document.cookie' })).slice(0, 400));
  say('close', text(await m.tool('browser_close', { session: SESSION, why: 'Flush the cookie store to disk before a second process reads it.' })).slice(0, 300));
  await new Promise(r => setTimeout(r, 2000));

  // The read, from a separate process, against a copy.
  const out = execFileSync(process.execPath, [path.join(__dirname, 'read-store.js'), path.join(SESSION, 'profile'), VALUE], { encoding: 'utf8' });
  report.reader = JSON.parse(out);
  console.log(JSON.stringify(report.reader, null, 2));

  say('destroy', text(await m.tool('browserai_destroy', { directory: SESSION, why: 'Row 95 is read; the session held a cookie and goes now.' })).slice(0, 300));
  m.close();
  server.close();
  fs.writeFileSync(path.join(OUT, 'report.json'), JSON.stringify(report, null, 2));
  setTimeout(() => process.exit(0), 800);
})().catch(e => { report.error = String((e && e.stack) || e); fs.writeFileSync(path.join(OUT, 'report.json'), JSON.stringify(report, null, 2)); console.error(report.error); process.exit(1); });
