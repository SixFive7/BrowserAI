// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Scratch rig (durability research, 2026-10-03), grown from .work\hard-kill\rig\server.js.
// The origin every run writes to and reads from: http://127.0.0.1:<port>. It also collects
// what each page reports, so the orchestrator never parses a snapshot.
// Per run it keeps a phase: "write" until the orchestrator says "read". In the read phase the
// write page is served inert (no Set-Cookie, no script), so a tab the browser restores cannot
// write again and spoil the read-back.
'use strict';
const http = require('node:http');
const fs = require('node:fs');

const port = Number(process.argv[2]);
const logFile = process.argv[3];
const runs = new Map();

function entry(run) {
  if (!runs.has(run)) runs.set(run, { phase: 'write', tabs: { write: {} }, writeHits: { write: 0 } });
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
  const marks = {};
  if (tag !== 'a') await fetch('/setcookie?run=' + encodeURIComponent(run) + '&tag=' + tag, { cache: 'no-store' });
  document.cookie = 'hk_js_' + tag + '=' + run + '; max-age=86400; path=/; samesite=lax';
  marks.cookie = Date.now();
  for (let i = 0; i < 40; i++) localStorage.setItem('hk_ls_' + tag + '_' + String(i).padStart(2, '0'), run + '_' + i);
  marks.localStorage = Date.now();
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
  marks.indexedDb = Date.now();
  let cacheOk = 'unavailable';
  if (typeof caches !== 'undefined') {
    const c = await caches.open('hk');
    await c.put(new Request('/hk-cache-' + tag), new Response(run + '_cache', { headers: { 'content-type': 'text/plain' } }));
    cacheOk = 'put';
  }
  marks.cacheStorage = Date.now();
  const tDone = Date.now();
  const done = { run, tag, t0, tDone, marks, cacheOk };
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

const inertPage = `<!doctype html><meta charset="utf-8"><title>hk-inert</title><body><pre>inert in the read phase</pre>`;

// A tab for the session-restore measurement. On its first load it sets a sessionStorage key;
// a restored tab finds the key and says so, which is how a restore of sessionStorage is seen.
const tabPage = `<!doctype html><meta charset="utf-8"><title>hk-tab</title><body><pre id="out">tab</pre>
<script>
(async () => {
  const q = new URLSearchParams(location.search);
  const run = q.get('run'), k = q.get('k');
  const had = sessionStorage.getItem('hk_tab');
  if (!had) sessionStorage.setItem('hk_tab', run + '_' + k);
  document.title = 'tab-' + k;
  await fetch('/tabseen?run=' + encodeURIComponent(run) + '&k=' + k + '&ss=' + encodeURIComponent(had || ''), { cache: 'no-store' });
})();
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
  out.cache = {};
  try {
    if (typeof caches === 'undefined') out.cache = { error: 'unavailable' };
    else {
      out.cacheNames = await caches.keys();
      if (out.cacheNames.includes('hk')) {
        const c = await caches.open('hk');
        for (const tag of ['a', 'b', 'w0']) {
          const m = await c.match('/hk-cache-' + tag);
          if (m) out.cache[tag] = await m.text();
        }
      }
    }
  } catch (e) { out.cache = { error: String(e) }; }
  out.tRead = Date.now();
  window.__read = out;
  document.getElementById('out').textContent = 'READ-OK ' + JSON.stringify(out);
  document.title = 'read';
  await fetch('/report?run=' + encodeURIComponent(run), { method: 'POST', body: JSON.stringify(out), cache: 'no-store' });
})().catch(e => { window.__read = { error: String(e) }; });
</script>`;

// The cost workload: for <secs> seconds, a localStorage write every 100 ms (20 rotating keys of
// 1 KiB), a persistent cookie every second, an IndexedDB put every 2 s and a history.pushState
// every 5 s (a session-history change, which session restore records). It measures how late
// a 50 ms interval timer fires, which is the page-visible cost of anything that blocks the
// renderer's main thread.
const workPage = `<!doctype html><meta charset="utf-8"><title>hk-work</title><body><pre id="out">working</pre>
<script>
(async () => {
  const q = new URLSearchParams(location.search);
  const run = q.get('run'), secs = Number(q.get('secs') || 60);
  const val = 'x'.repeat(1024);
  const lags = [];
  let ops = { ls: 0, cookie: 0, idb: 0, push: 0 }, lsMs = 0;
  const db = await new Promise((res, rej) => { const r = indexedDB.open('hkwork', 1); r.onupgradeneeded = () => r.result.createObjectStore('s'); r.onsuccess = () => res(r.result); r.onerror = () => rej(r.error); });
  const t0 = performance.now();
  let last = t0;
  await new Promise(resolve => {
    let n = 0;
    const iv = setInterval(() => {
      const now = performance.now();
      lags.push(now - last - 50);
      last = now;
      n++;
      if (n % 2 === 0) { const a = performance.now(); localStorage.setItem('w' + (n % 20), val + n); lsMs += performance.now() - a; ops.ls++; }
      if (n % 20 === 0) { document.cookie = 'hk_w' + (n % 5) + '=' + n + '; max-age=86400; path=/'; ops.cookie++; }
      if (n % 40 === 0) { db.transaction('s', 'readwrite').objectStore('s').put(val + n, 'k' + (n % 10)); ops.idb++; }
      if (n % 100 === 0) { history.pushState({ n }, '', '?run=' + encodeURIComponent(run) + '&secs=' + secs + '&n=' + n); ops.push++; }
      if (now - t0 >= secs * 1000) { clearInterval(iv); resolve(); }
    }, 50);
  });
  lags.sort((a, b) => a - b);
  const pick = p => lags[Math.min(lags.length - 1, Math.floor(p * lags.length))];
  const out = { run, secs, samples: lags.length, lagP50: pick(0.5), lagP95: pick(0.95), lagP99: pick(0.99), lagMax: lags[lags.length - 1], ops, lsSetItemMsTotal: lsMs, elapsedMs: performance.now() - t0 };
  window.__work = out;
  document.getElementById('out').textContent = 'WORK-OK ' + JSON.stringify(out);
  await fetch('/workdone?run=' + encodeURIComponent(run), { method: 'POST', body: JSON.stringify(out), cache: 'no-store' });
})().catch(e => { window.__work = { error: String(e) }; });
</script>`;

// Added 2026-10-03 by the replacement agent: a form-and-scroll cost workload. Firefox's content-side
// session collector (SessionStoreChangeListener) batches input and scroll changes for
// browser.sessionstore.interval ms, so this page exercises exactly what that pref paces: 300 text
// fields the driver fills with trusted input (Playwright fill), and a window scroll every 50 ms.
// It measures how late a 50 ms interval timer fires, as the work page does.
const formPage = `<!doctype html><meta charset="utf-8"><title>hk-form</title><body>
<form id="f" autocomplete="off"></form><div style="height:40000px">tall</div><pre id="out">working</pre>
<script>
(async () => {
  const q = new URLSearchParams(location.search);
  const run = q.get('run'), secs = Number(q.get('secs') || 60), fields = Number(q.get('fields') || 300);
  const f = document.getElementById('f');
  for (let i = 0; i < fields; i++) { const inp = document.createElement('input'); inp.type = 'text'; inp.id = 'f' + i; inp.name = 'f' + i; f.appendChild(inp); f.appendChild(document.createElement('br')); }
  let inputs = 0, scrolls = 0;
  document.addEventListener('input', () => { inputs++; }, true);
  window.__formReady = true;
  const lags = [];
  const t0 = performance.now();
  let last = t0, dir = 1;
  await new Promise(resolve => {
    const iv = setInterval(() => {
      const now = performance.now();
      lags.push(now - last - 50);
      last = now;
      window.scrollBy(0, dir * 150);
      if (window.scrollY > 30000) dir = -1; else if (window.scrollY < 150) dir = 1;
      scrolls++;
      if (now - t0 >= secs * 1000) { clearInterval(iv); resolve(); }
    }, 50);
  });
  lags.sort((a, b) => a - b);
  const pick = p => lags[Math.min(lags.length - 1, Math.floor(p * lags.length))];
  const out = { run, secs, fields, samples: lags.length, lagP50: pick(0.5), lagP95: pick(0.95), lagP99: pick(0.99), lagMax: lags[lags.length - 1], ops: { input: inputs, scroll: scrolls }, elapsedMs: performance.now() - t0 };
  window.__work = out;
  document.getElementById('out').textContent = 'FORM-OK ' + JSON.stringify(out);
  await fetch('/workdone?run=' + encodeURIComponent(run), { method: 'POST', body: JSON.stringify(out), cache: 'no-store' });
})().catch(e => { window.__work = { error: String(e) }; });
</script>`;

function readBody(req, cb) {
  let body = '';
  req.setEncoding('utf8');
  req.on('data', d => { body += d; });
  req.on('end', () => cb(body));
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1');
  const run = url.searchParams.get('run') || '';
  const nostore = { 'cache-control': 'no-store' };
  if (url.pathname === '/write') {
    const e = entry(run);
    const tag = url.searchParams.get('tag') || 'a';
    e.writeHits[e.phase] = (e.writeHits[e.phase] || 0) + 1;
    log({ ev: 'write-served', run, tag, phase: e.phase, cookie: req.headers.cookie || '' });
    if (e.phase !== 'write') {
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', ...nostore });
      res.end(inertPage);
      return;
    }
    res.writeHead(200, {
      'content-type': 'text/html; charset=utf-8', ...nostore,
      'set-cookie': `hk_srv_${tag}=${run}; Max-Age=86400; Path=/; HttpOnly; SameSite=Lax`,
    });
    res.end(writePage);
    return;
  }
  if (url.pathname === '/prime') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', ...nostore });
    res.end('<!doctype html><meta charset="utf-8"><title>hk-prime</title><body>prime');
    return;
  }
  if (url.pathname === '/tab') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', ...nostore });
    res.end(tabPage);
    return;
  }
  if (url.pathname === '/tabseen') {
    const e = entry(run);
    const k = url.searchParams.get('k');
    const ss = url.searchParams.get('ss') || '';
    const bucket = e.tabs[e.phase] || (e.tabs[e.phase] = {});
    bucket[k] = { at: Date.now(), ss };
    log({ ev: 'tabseen', run, k, ss, phase: e.phase });
    res.writeHead(204, nostore);
    res.end();
    return;
  }
  if (url.pathname === '/phase') {
    const e = entry(run);
    e.phase = url.searchParams.get('p') || 'read';
    log({ ev: 'phase', run, phase: e.phase });
    res.writeHead(204, nostore);
    res.end();
    return;
  }
  if (url.pathname === '/setcookie') {
    const tag = url.searchParams.get('tag') || 'b';
    log({ ev: 'setcookie-served', run, tag, cookie: req.headers.cookie || '' });
    res.writeHead(204, { ...nostore, 'set-cookie': `hk_srv_${tag}=${run}; Max-Age=86400; Path=/; HttpOnly; SameSite=Lax` });
    res.end();
    return;
  }
  if (url.pathname === '/done') {
    const tag = url.searchParams.get('tag') || 'a';
    const e = entry(run);
    e.done = e.done || {};
    e.done[tag] = { t0: Number(url.searchParams.get('t0')), t: Number(url.searchParams.get('t')), error: url.searchParams.get('error'), at: Date.now() };
    log({ ev: 'done', run, tag, done: e.done[tag] });
    res.writeHead(204, nostore);
    res.end();
    return;
  }
  if (url.pathname === '/read') {
    const e = entry(run);
    e.readCookieHeader = req.headers.cookie || '';
    log({ ev: 'read-served', run, cookie: e.readCookieHeader });
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', ...nostore });
    res.end(readPage);
    return;
  }
  if (url.pathname === '/work') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', ...nostore });
    res.end(workPage);
    return;
  }
  if (url.pathname === '/form') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', ...nostore });
    res.end(formPage);
    return;
  }
  if ((url.pathname === '/report' || url.pathname === '/workdone') && req.method === 'POST') {
    readBody(req, body => {
      const e = entry(run);
      const key = url.pathname === '/report' ? 'report' : 'work';
      try { e[key] = JSON.parse(body); } catch { e[key] = { unparsable: body }; }
      e[key + 'At'] = Date.now();
      log({ ev: key, run });
      res.writeHead(204, nostore);
      res.end();
    });
    return;
  }
  if (url.pathname === '/status') {
    res.writeHead(200, { 'content-type': 'application/json', ...nostore });
    res.end(JSON.stringify(runs.get(run) || {}));
    return;
  }
  res.writeHead(404, nostore);
  res.end();
});

server.listen(port, '127.0.0.1', () => {
  process.stdout.write(JSON.stringify({ listening: server.address().port, pid: process.pid }) + '\n');
});
