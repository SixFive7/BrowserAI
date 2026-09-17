// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Re-establishes kb/playwright/provisioning-and-timings.md's resume entry
// (re-verification row 38): kill the node child, resume against the directory,
// and read back cookies, localStorage, sessionStorage, IndexedDB, service
// workers and CacheStorage.
//
// Nothing here touches %LocalAppData%\BrowserAI.app. The session directory is
// the one passed in; the data root is the product's own, which is what the
// suite drives too.
'use strict';
const { spawn, execFileSync } = require('node:child_process');
const path = require('node:path');
const fs = require('node:fs');
const { Mcp, text } = require('./mcp.js');

const exe = process.argv[2];
const sessionDir = process.argv[3];
const outPath = process.argv[4];
const browser = process.argv[5] || 'chromium';

const report = { browser, steps: [], utc: new Date().toISOString() };
const say = (k, v) => { report[k] = v; console.log(k, '=', typeof v === 'string' ? v.slice(0, 400) : JSON.stringify(v).slice(0, 400)); };

// The writes. One function, so a single evaluate both writes and confirms.
const WRITE = `async () => {
  document.cookie = 'reverify=' + 'cookie-value' + '; path=/; max-age=3600';
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
  const local = localStorage.getItem('reverify');
  const session = sessionStorage.getItem('reverify');
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
        const m = await c.match('/cached'); cache = m ? await m.text() : null; } catch { cache = null; }
  const regs = await navigator.serviceWorker.getRegistrations();
  return JSON.stringify({ cookie, local, session, idb, cache, serviceWorkers: regs.length });
}`;

function nodeChildrenOf(pid, ownedPrefix) {
  // pid-keyed, never by image name: enumerate by ParentProcessId and keep only
  // children whose ExecutablePath is under a directory BrowserAI owns.
  const ps = `Get-CimInstance Win32_Process -Filter "ParentProcessId=${pid}" | ForEach-Object { '{0}|{1}' -f $_.ProcessId, $_.ExecutablePath }`;
  const out = execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8' });
  return out.split(/\r?\n/).map(l => l.trim()).filter(Boolean)
    .map(l => { const i = l.indexOf('|'); return { pid: Number(l.slice(0, i)), exe: l.slice(i + 1) }; })
    .filter(c => c.exe && c.exe.toLowerCase().startsWith(ownedPrefix.toLowerCase()));
}

function killVerified(pid, exe) {
  execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command',
    `$p = Get-Process -Id ${pid} -ErrorAction Stop; if ($p.Path -ne '${exe.replace(/'/g, "''")}') { throw 'identity changed' }; Stop-Process -Id ${pid} -Force`],
    { encoding: 'utf8' });
}

(async () => {
  const pageServer = spawn(process.execPath, [path.join(__dirname, 'page-server.js'), '0'],
    { stdio: ['ignore', 'pipe', 'inherit'], windowsHide: true });
  const port = await new Promise(res => pageServer.stdout.once('data', d => res(JSON.parse(d.toString()).port)));
  const url = `http://127.0.0.1:${port}/`;
  say('origin', url);

  const ownedPayload = path.join(path.dirname(exe), 'payload');
  const m = new Mcp(exe);
  await m.initialize();

  const init = await m.tool('browserai_init', {
    directory: sessionDir,
    purpose: 'Re-establishing the resume cost and what a resume preserves, for kb re-verification row 38 on 2026-09-16.',
    browser,
  });
  say('initMs', init.ms);
  say('init', text(init).slice(0, 600));

  const nav1 = await m.tool('browser_navigate', { session: sessionDir, why: 'Open a real origin so the storage written next belongs to one.', url });
  say('navigate1Ms', nav1.ms);

  const wrote = await m.tool('browser_evaluate', { session: sessionDir, why: 'Write one value into each durable store so the resume has something to preserve.', function: WRITE });
  say('wrote', text(wrote).slice(0, 400));

  const before = await m.tool('browser_evaluate', { session: sessionDir, why: 'Confirm every store really holds a value before the child is killed.', function: READ });
  say('before', text(before).slice(0, 400));

  const children = nodeChildrenOf(m.proc.pid, ownedPayload);
  say('nodeChildren', children);
  if (children.length === 0) throw new Error('no node child under a path BrowserAI owns');
  for (const c of children) killVerified(c.pid, c.exe);
  say('killed', children.map(c => c.pid));

  await new Promise(r => setTimeout(r, 1500));

  const resume = await m.tool('browserai_resume', { directory: sessionDir, why: 'Time the resume and read back what survived the child being killed.' });
  say('resumeMs', resume.ms);
  say('resume', text(resume).slice(0, 800));

  const nav2 = await m.tool('browser_navigate', { session: sessionDir, why: 'Return to the same origin so its storage is readable again.', url });
  say('navigate2Ms', nav2.ms);

  const after = await m.tool('browser_evaluate', { session: sessionDir, why: 'Read every store back and record exactly which survived the resume.', function: READ });
  say('after', text(after).slice(0, 400));

  const destroy = await m.tool('browserai_destroy', { directory: sessionDir, why: 'The probe is finished; leave nothing in the session index.' });
  say('destroy', text(destroy).slice(0, 400));

  m.close();
  pageServer.kill();
  report.stderrTail = m.stderr.slice(-2000);
  fs.writeFileSync(outPath, JSON.stringify(report, null, 2), 'utf8');
  setTimeout(() => process.exit(0), 500);
})().catch(err => {
  report.error = String(err && err.stack || err);
  try { fs.writeFileSync(outPath, JSON.stringify(report, null, 2), 'utf8'); } catch { /* nothing left to do */ }
  console.error(report.error);
  process.exit(1);
});
