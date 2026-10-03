// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Re-establishes kb/playwright/provisioning-and-timings.md's resume entry
// (re-verification row 38): the node child is gone, a second BrowserAI meets
// the directory, and cookies, localStorage, sessionStorage, IndexedDB, service
// workers and CacheStorage are read back.
//
// Two server processes, which is the case the feature exists for: a session
// created, closed, and met again by a different process. Nothing here touches
// %LocalAppData%\BrowserAI.app.
'use strict';
const { spawn, execFileSync } = require('node:child_process');
const path = require('node:path');
const fs = require('node:fs');
const { Mcp, text } = require('./mcp.js');

const exe = process.argv[2];
const sessionDir = process.argv[3];
const outPath = process.argv[4];
const browser = process.argv[5] || 'chromium';

const report = { browser, sessionDir, utc: new Date().toISOString() };
const say = (k, v) => { report[k] = v; console.log(k, '=', typeof v === 'string' ? v.slice(0, 500) : JSON.stringify(v).slice(0, 500)); };

const WRITE = `async () => {
  document.cookie = 'reverify=cookie-value; path=/; max-age=3600';
  localStorage.setItem('reverify', 'local-value');
  sessionStorage.setItem('reverify', 'session-value');
  await new Promise((res, rej) => {
    const r = indexedDB.open('reverify-db', 1);
    r.onupgradeneeded = () => r.result.createObjectStore('kv');
    r.onsuccess = () => { const db = r.result;
      const tx = db.transaction('kv', 'readwrite');
      tx.objectStore('kv').put('idb-value', 'reverify');
      tx.oncomplete = () => { db.close(); res(); };
      tx.onerror = () => rej(tx.error); };
    r.onerror = () => rej(r.error);
  });
  const cache = await caches.open('reverify-cache');
  await cache.put('/cached', new Response('cache-value'));
  const reg = await navigator.serviceWorker.register('/sw.js');
  await navigator.serviceWorker.ready;
  return 'wrote:' + (reg.scope ? 'sw-ok' : 'sw-missing');
}`;

const READ = `async () => {
  const cookie = (document.cookie.match(/(?:^|; )reverify=([^;]*)/) || [])[1] || null;
  const idb = await new Promise(res => {
    const r = indexedDB.open('reverify-db', 1);
    r.onsuccess = () => { const db = r.result;
      if (!db.objectStoreNames.contains('kv')) { db.close(); return res(null); }
      const g = db.transaction('kv', 'readonly').objectStore('kv').get('reverify');
      g.onsuccess = () => { db.close(); res(g.result ?? null); };
      g.onerror = () => { db.close(); res(null); }; };
    r.onerror = () => res(null);
  });
  let cache = null;
  try { const c = await caches.open('reverify-cache');
        const mm = await c.match('/cached'); cache = mm ? await mm.text() : null; } catch { cache = null; }
  const regs = await navigator.serviceWorker.getRegistrations();
  return JSON.stringify({
    cookie,
    local: localStorage.getItem('reverify'),
    session: sessionStorage.getItem('reverify'),
    idb, cache, serviceWorkers: regs.length });
}`;

function childrenUnder(pid, ownedPrefix) {
  // pid-keyed, never by image name: enumerated by ParentProcessId and kept only
  // when the executable path is under a directory BrowserAI owns.
  const ps = `Get-CimInstance Win32_Process -Filter "ParentProcessId=${pid}" | ForEach-Object { '{0}|{1}' -f $_.ProcessId, $_.ExecutablePath }`;
  let out = '';
  try { out = execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8' }); } catch { return []; }
  return out.split(/\r?\n/).map(l => l.trim()).filter(Boolean)
    .map(l => { const i = l.indexOf('|'); return { pid: Number(l.slice(0, i)), exe: l.slice(i + 1) }; })
    .filter(c => c.exe && c.exe.toLowerCase().startsWith(ownedPrefix.toLowerCase()));
}

function aliveVerified(pid, exe) {
  const ps = `try { $p = Get-Process -Id ${pid} -ErrorAction Stop; if ($p.Path -eq '${exe.replace(/'/g, "''")}') { 'ALIVE' } else { 'RECYCLED' } } catch { 'GONE' }`;
  return execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8' }).trim();
}

const wait = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const pageServer = spawn(process.execPath, [path.join(__dirname, 'page-server.js'), '0'],
    { stdio: ['ignore', 'pipe', 'inherit'], windowsHide: true });
  const port = await new Promise(res => pageServer.stdout.once('data', d => res(JSON.parse(d.toString()).port)));
  const url = `http://127.0.0.1:${port}/`;
  say('origin', url);

  const ownedPayload = path.join(path.dirname(exe), 'payload');

  // ---- Phase A: create the session and fill every store.
  const a = new Mcp(exe);
  await a.initialize();
  const init = await a.tool('browserai_init', {
    directory: sessionDir,
    purpose: 'Re-establishing the resume cost and what a resume preserves, for kb re-verification row 38 on 2026-09-16.',
    browser,
  });
  say('initMs', init.ms);
  say('initText', text(init).slice(0, 300));
  if (init.error || (init.result && init.result.isError)) { throw new Error('init refused: ' + text(init)); }

  say('navigate1Ms', (await a.tool('browser_navigate', { session: sessionDir, why: 'Open a real origin so the storage written next belongs to one.', url })).ms);
  say('wrote', text(await a.tool('browser_evaluate', { session: sessionDir, why: 'Write one value into each durable store so the resume has something to preserve.', function: WRITE })));
  say('before', text(await a.tool('browser_evaluate', { session: sessionDir, why: 'Confirm every store really holds a value before the child goes away.', function: READ })));

  const children = childrenUnder(a.proc.pid, ownedPayload);
  say('nodeChildren', children);

  // The node child goes with the server that owns it: close A's stdin and let
  // the job take the tree down, which is the product's own teardown.
  a.close();
  await wait(4000);
  say('childStates', children.map(c => ({ pid: c.pid, state: aliveVerified(c.pid, c.exe) })));
  say('serverAExited', aliveVerified(a.proc.pid, exe));

  // ---- Phase B: a different process meets the directory.
  const b = new Mcp(exe);
  await b.initialize();
  const resume = await b.tool('browserai_resume', { directory: sessionDir, why: 'Time the resume and read back what survived the session being closed.' });
  say('resumeMs', resume.ms);
  say('resume', text(resume));

  say('navigate2Ms', (await b.tool('browser_navigate', { session: sessionDir, why: 'Return to the same origin so its storage is readable again.', url })).ms);
  say('after', text(await b.tool('browser_evaluate', { session: sessionDir, why: 'Read every store back and record exactly which survived the resume.', function: READ })));

  say('destroy', text(await b.tool('browserai_destroy', { directory: sessionDir, why: 'The probe is finished; leave nothing in the session index.' })));

  b.close();
  pageServer.kill();
  report.stderrTailA = a.stderr.slice(-1500);
  report.stderrTailB = b.stderr.slice(-1500);
  fs.writeFileSync(outPath, JSON.stringify(report, null, 2), 'utf8');
  await wait(600);
  process.exit(0);
})().catch(err => {
  report.error = String((err && err.stack) || err);
  try { fs.writeFileSync(outPath, JSON.stringify(report, null, 2), 'utf8'); } catch { /* nothing left to do */ }
  console.error(report.error);
  process.exit(1);
});
