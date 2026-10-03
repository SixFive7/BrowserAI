// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch rig: the origin every run writes to and reads from. http://127.0.0.1:<port>.
// It also collects what each page reports, so the orchestrator never parses a snapshot.
'use strict';
const http = require('node:http');
const fs = require('node:fs');

const port = Number(process.argv[2]);
const logFile = process.argv[3];
const runs = new Map();

function entry(run) {
  if (!runs.has(run)) runs.set(run, {});
  return runs.get(run);
}

function log(o) {
  fs.appendFileSync(logFile, JSON.stringify({ t: Date.now(), ...o }) + '\n');
}

const writePage = `<!doctype html><meta charset="utf-8"><title>hk-write</title><body><pre id="out">writing</pre>
<script>
window.__hkWrite = async (tag) => {
  const run = new URLSearchParams(location.search).get('run');
  const t0 = Date.now();
  if (tag !== 'a') await fetch('/setcookie?run=' + encodeURIComponent(run) + '&tag=' + tag, { cache: 'no-store' });
  document.cookie = 'hk_js_' + tag + '=' + run + '; max-age=86400; path=/; samesite=lax';
  for (let i = 0; i < 40; i++) localStorage.setItem('hk_ls_' + tag + '_' + String(i).padStart(2, '0'), run + '_' + i);
  sessionStorage.setItem('hk_ss_' + tag, run);
  await new Promise((resolve, reject) => {
    const r = indexedDB.open('hkdb', 1);
    r.onupgradeneeded = () => r.result.createObjectStore('s');
    r.onerror = () => reject(r.error);
    r.onsuccess = () => {
      const db = r.result;
      const tx = db.transaction('s', 'readwrite');
      tx.objectStore('s').put(run, 'k_' + tag);
      tx.oncomplete = () => { db.close(); resolve(); };
      tx.onerror = () => reject(tx.error);
      tx.onabort = () => reject(tx.error || new Error('abort'));
    };
  });
  const tDone = Date.now();
  const done = { run, tag, t0, tDone };
  window.__done = done;
  document.getElementById('out').textContent = 'WRITTEN-OK ' + run + ' ' + tag + ' ' + tDone;
  document.title = 'written-' + tag;
  await fetch('/done?run=' + encodeURIComponent(run) + '&tag=' + tag + '&t0=' + t0 + '&t=' + tDone, { cache: 'no-store' });
  return done;
};
window.__hkWrite(new URLSearchParams(location.search).get('tag') || 'a').catch(e => {
  window.__done = { error: String(e) };
  fetch('/done?run=' + encodeURIComponent(new URLSearchParams(location.search).get('run')) + '&error=' + encodeURIComponent(String(e)), { cache: 'no-store' });
});
</script>`;

const readPage = `<!doctype html><meta charset="utf-8"><title>hk-read</title><body><pre id="out">reading</pre>
<script>
(async () => {
  const run = new URLSearchParams(location.search).get('run');
  const out = { run };
  out.documentCookie = document.cookie;
  const ls = {};
  for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i); ls[k] = localStorage.getItem(k); }
  out.localStorageLength = localStorage.length;
  out.localStorage = ls;
  const ss = {};
  for (let i = 0; i < sessionStorage.length; i++) { const k = sessionStorage.key(i); ss[k] = sessionStorage.getItem(k); }
  out.sessionStorage = ss;
  let dbs = [];
  try { dbs = (await indexedDB.databases()).map(d => d.name + '@' + d.version); } catch (e) { dbs = ['error:' + e]; }
  out.idbDatabases = dbs;
  out.idb = null;
  if (dbs.some(d => d.startsWith('hkdb@'))) {
    out.idb = await new Promise((resolve) => {
      const r = indexedDB.open('hkdb');
      r.onerror = () => resolve({ error: 'open:' + r.error });
      r.onsuccess = () => {
        const db = r.result;
        try {
          const tx = db.transaction('s', 'readonly');
          const all = {};
          const c = tx.objectStore('s').openCursor();
          c.onsuccess = () => { const cur = c.result; if (cur) { all[cur.key] = cur.value; cur.continue(); } else { db.close(); resolve(all); } };
          c.onerror = () => resolve({ error: 'cursor' });
        } catch (e) { resolve({ error: 'tx:' + e }); }
      };
    });
  }
  out.tRead = Date.now();
  window.__read = out;
  document.getElementById('out').textContent = 'READ-OK ' + JSON.stringify(out);
  document.title = 'read';
  await fetch('/report?run=' + encodeURIComponent(run), { method: 'POST', body: JSON.stringify(out), cache: 'no-store' });
})().catch(e => { window.__read = { error: String(e) }; });
</script>`;

const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1');
  const run = url.searchParams.get('run') || '';
  if (url.pathname === '/write') {
    const tag = url.searchParams.get('tag') || 'a';
    log({ ev: 'write-served', run, tag, cookie: req.headers.cookie || '' });
    res.writeHead(200, {
      'content-type': 'text/html; charset=utf-8',
      'cache-control': 'no-store',
      'set-cookie': `hk_srv_${tag}=${run}; Max-Age=86400; Path=/; HttpOnly; SameSite=Lax`,
    });
    res.end(writePage);
    return;
  }
  if (url.pathname === '/setcookie') {
    const tag = url.searchParams.get('tag') || 'b';
    log({ ev: 'setcookie-served', run, tag, cookie: req.headers.cookie || '' });
    res.writeHead(204, {
      'cache-control': 'no-store',
      'set-cookie': `hk_srv_${tag}=${run}; Max-Age=86400; Path=/; HttpOnly; SameSite=Lax`,
    });
    res.end();
    return;
  }
  if (url.pathname === '/done') {
    const tag = url.searchParams.get('tag') || 'a';
    const e = entry(run);
    e.done = e.done || {};
    e.done[tag] = { t0: Number(url.searchParams.get('t0')), t: Number(url.searchParams.get('t')), error: url.searchParams.get('error'), at: Date.now() };
    log({ ev: 'done', run, tag, done: e.done[tag] });
    res.writeHead(204, { 'cache-control': 'no-store' });
    res.end();
    return;
  }
  if (url.pathname === '/read') {
    const e = entry(run);
    e.readCookieHeader = req.headers.cookie || '';
    log({ ev: 'read-served', run, cookie: e.readCookieHeader });
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end(readPage);
    return;
  }
  if (url.pathname === '/report' && req.method === 'POST') {
    let body = '';
    req.setEncoding('utf8');
    req.on('data', d => { body += d; });
    req.on('end', () => {
      const e = entry(run);
      try { e.report = JSON.parse(body); } catch { e.report = { unparsable: body }; }
      e.reportAt = Date.now();
      log({ ev: 'report', run });
      res.writeHead(204, { 'cache-control': 'no-store' });
      res.end();
    });
    return;
  }
  if (url.pathname === '/status') {
    res.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'no-store' });
    res.end(JSON.stringify(runs.get(run) || {}));
    return;
  }
  res.writeHead(404, { 'cache-control': 'no-store' });
  res.end();
});

server.listen(port, '127.0.0.1', () => {
  process.stdout.write(JSON.stringify({ listening: server.address().port, pid: process.pid }) + '\n');
});
