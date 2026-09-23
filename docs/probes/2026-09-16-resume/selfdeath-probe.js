// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Q223c: does a browser server that dies of its OWN accord flush what a killed
// one loses? Path B in resume-probe2.js kills the node child by pid, and
// kb/playwright/provisioning-and-timings.md records that persistent stores go
// with it. The open hazard row says the mechanism is a READING and not a
// measurement, and names the one thing that would settle it: a browser that
// dies without being killed may flush on the way out.
//
// Three modes over one arrangement, so the only thing that varies is HOW the
// child goes:
//
//   kill  -- Stop-Process by pid, identity verified against a path BrowserAI
//           owns. The control: this is exactly resume-probe2.js's Path B.
//   exit  -- the child calls process.exit(0) on ITSELF, through
//           browser_run_code_unsafe, which upstream documents as running in the
//           Playwright server process. Node's own exit path runs.
//   abort -- the child calls process.abort() on itself. A real abnormal
//           termination by its own hand, with no exit handlers at all.
//
// `exit` and `abort` are both deaths BrowserAI did not cause and did not ask
// for, which is the population the model-facing string has to be true of.
// Nothing here touches %LocalAppData%\BrowserAI.app.
'use strict';
const { spawn, execFileSync } = require('node:child_process');
const path = require('node:path');
const fs = require('node:fs');
const { Mcp, text } = require('./mcp.js');

const exe = process.argv[2];
const sessionDir = process.argv[3];
const outPath = process.argv[4];
const mode = process.argv[5] || 'exit';
const browser = process.argv[6] || 'chromium';

if (!['kill', 'exit', 'abort'].includes(mode)) {
  console.error(`mode must be kill, exit or abort; got '${mode}'`);
  process.exit(2);
}

const report = { mode, browser, sessionDir, utc: new Date().toISOString() };
const say = (k, v) => { report[k] = v; console.log(k, '=', typeof v === 'string' ? v.slice(0, 500) : JSON.stringify(v).slice(0, 500)); };

// The same WRITE and READ resume-probe2.js uses, character for character, so
// the two measurements are comparable without an argument about the probe.
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

function aliveVerified(pid, exePath) {
  const ps = `try { $p = Get-Process -Id ${pid} -ErrorAction Stop; if ($p.Path -eq '${exePath.replace(/'/g, "''")}') { 'ALIVE' } else { 'RECYCLED' } } catch { 'GONE' }`;
  return execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8' }).trim();
}

/**
 * Waits for every recorded child to stop being itself, polling INSIDE one
 * PowerShell instead of starting one per poll.
 *
 * ⚠️ This is one call on purpose, and the reason is measured. A first version
 * polled `aliveVerified` in a JavaScript loop every 500 ms, which starts two
 * `pwsh` processes per iteration; under the load of the thing being measured
 * each start cost seconds, so a 30-second budget took ELEVEN MINUTES of wall
 * clock and the probe read as hung. The identity check is unchanged -- pid AND
 * image path, never a name -- it has just moved inside the loop that uses it.
 */
function waitForChildrenToGo(children, boundMs) {
  const checks = children
    .map(c => `@{ Pid = ${c.pid}; Path = '${c.exe.replace(/'/g, "''")}' }`)
    .join(', ');

  const ps = `
    $targets = @(${checks})
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt ${boundMs}) {
      $states = foreach ($t in $targets) {
        $p = Get-Process -Id $t.Pid -ErrorAction SilentlyContinue
        if (-not $p) { 'GONE' } elseif ($p.Path -ne $t.Path) { 'RECYCLED' } else { 'ALIVE' }
      }
      if ($states -notcontains 'ALIVE') { break }
      Start-Sleep -Milliseconds 250
    }
    [pscustomobject]@{ ms = [int]$sw.ElapsedMilliseconds; states = @($states) } | ConvertTo-Json -Compress`;

  return JSON.parse(execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8' }));
}

function killVerified(pid, exePath) {
  execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command',
    `$p = Get-Process -Id ${pid} -ErrorAction Stop; if ($p.Path -ne '${exePath.replace(/'/g, "''")}') { throw 'identity changed' }; Stop-Process -Id ${pid} -Force`],
    { encoding: 'utf8' });
}

// Counts the browser processes under the session's own browsers root, by pid
// and by a path BrowserAI owns -- never by image name.
function browsersUnder(browsersRoot) {
  const ps = `@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.ToLowerInvariant().StartsWith('${browsersRoot.toLowerCase().replace(/'/g, "''")}') }).Count`;
  try { return Number(execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8' }).trim()); }
  catch { return -1; }
}

const wait = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const pageServer = spawn(process.execPath, [path.join(__dirname, 'page-server.js'), '0'],
    { stdio: ['ignore', 'pipe', 'inherit'], windowsHide: true });
  const port = await new Promise(res => pageServer.stdout.once('data', d => res(JSON.parse(d.toString()).port)));
  const url = `http://127.0.0.1:${port}/`;
  say('origin', url);

  const ownedPayload = path.join(path.dirname(exe), 'payload');
  const browsersRoot = path.join(process.env.LOCALAPPDATA, 'BrowserAI', 'browsers');

  const a = new Mcp(exe);
  await a.initialize();
  const init = await a.tool('browserai_init', {
    directory: sessionDir,
    purpose: 'Q223c: measuring whether a browser server that dies of its own accord flushes what a killed one loses.',
    browser,
  });
  say('initMs', init.ms);
  if (init.error || (init.result && init.result.isError)) { throw new Error('init refused: ' + text(init)); }

  say('navigate1Ms', (await a.tool('browser_navigate', { session: sessionDir, why: 'Open a real origin so the storage written next belongs to one.', url })).ms);
  say('wrote', text(await a.tool('browser_evaluate', { session: sessionDir, why: 'Write one value into each durable store so the relaunch has something to preserve.', function: WRITE })));
  say('before', text(await a.tool('browser_evaluate', { session: sessionDir, why: 'Confirm every store really holds a value before the child goes away.', function: READ })));

  const children = childrenUnder(a.proc.pid, ownedPayload);
  say('nodeChildren', children);
  say('browsersBefore', browsersUnder(browsersRoot));
  if (children.length === 0) { throw new Error('no node child found under a path BrowserAI owns; nothing to make die'); }

  // ---- The child goes. The mode is the whole experiment.
  say('deathAt', new Date().toISOString());
  if (mode === 'kill') {
    for (const c of children) killVerified(c.pid, c.exe);
  } else {
    // ⚠️ THE `page.constructor.constructor` HOP IS NOT CLEVERNESS, IT IS THE
    // ONLY ROUTE, and it is a measured property of upstream, not a
    // guess. `browser_run_code_unsafe` describes itself as executing "arbitrary
    // JavaScript in the Playwright server process", and it does -- but through
    // `vm.runInContext` against a context built as `{ page, __end__ }` and
    // nothing else (read 2026-09-22 out of the payload's own
    // playwright-core/lib/coreBundle.js). So `process`, `setTimeout` and
    // `require` are all undefined in the snippet, and a first version of this
    // probe died on `ReferenceError: setTimeout is not defined` while reporting
    // a clean run, because the child it meant to kill never went anywhere.
    // `page` is a host-realm object, so `page.constructor` is the host's
    // Function and `.constructor('return process')()` returns the real one --
    // the escape node's own documentation says `vm` does not defend against.
    //
    // No delay: with `process` reached this way the call never answers, because
    // the process carrying the answer is gone. The probe does not wait on it --
    // it waits on the children actually being gone, below.
    const reach = `page.constructor.constructor('return process')()`;
    const snippet = mode === 'exit'
      ? `async (page) => { ${reach}.exit(0); return 'unreachable'; }`
      : `async (page) => { ${reach}.abort(); return 'unreachable'; }`;
    const fired = await Promise.race([
      a.tool('browser_run_code_unsafe', { session: sessionDir, why: 'Make the browser server end itself, so this death is one BrowserAI did not cause.', code: snippet }),
      wait(15000).then(() => ({ ms: -1, timedOut: true })),
    ]);
    say('selfDeathCall', fired.timedOut ? 'no answer within 15 s (the process carrying it had gone)' : text(fired).slice(0, 300));
  }

  // Wait for every recorded child to be gone, verified by pid AND path.
  const gone = waitForChildrenToGo(children, 30000);

  say('childStates', children.map((c, i) => ({ pid: c.pid, state: gone.states[i] })));
  say('msUntilChildrenGone', gone.ms);
  say('serverAStillAlive', aliveVerified(a.proc.pid, exe));
  await wait(3000);
  say('browsersAfterDeath', browsersUnder(browsersRoot));

  // ---- The same process repairs the session: this is Path B, the path the
  // model-facing string is returned on.
  const resume = await a.tool('browserai_resume', { directory: sessionDir, why: 'Read back which stores survived a browser server that ended itself.' });
  say('resumeMs', resume.ms);
  say('resume', text(resume));

  say('navigate2Ms', (await a.tool('browser_navigate', { session: sessionDir, why: 'Return to the same origin so its storage is readable again.', url })).ms);
  say('after', text(await a.tool('browser_evaluate', { session: sessionDir, why: 'Read every store back and record exactly which survived.', function: READ })));

  say('destroy', text(await a.tool('browserai_destroy', { directory: sessionDir, why: 'The probe is finished; leave nothing in the session index.' })).slice(0, 200));

  a.close();
  pageServer.kill();
  report.stderrTailA = a.stderr.slice(-1500);
  fs.writeFileSync(outPath, JSON.stringify(report, null, 2), 'utf8');
  await wait(600);
  process.exit(0);
})().catch(err => {
  report.error = String((err && err.stack) || err);
  try { fs.writeFileSync(outPath, JSON.stringify(report, null, 2), 'utf8'); } catch { /* nothing left to do */ }
  console.error(report.error);
  process.exit(1);
});
