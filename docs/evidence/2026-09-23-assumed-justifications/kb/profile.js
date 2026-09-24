// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Probes for two kb claims about one Chromium profile directory:
//   (1) velopack.md  "the damage is a lost session and not corruption" after a hard kill
//   (2) detection.md "Two browsers writing one profile's cookie and storage databases is silent corruption"
const { chromium } = require(process.env.CORE_PATH);
const { spawn, execFileSync } = require('child_process');
const fs = require('fs'), path = require('path'), http = require('http');
const { DatabaseSync } = require('node:sqlite');

const FULL = process.env.CHROME_EXE, SHELL = process.env.SHELL_EXE, BASE = process.env.BASE;
const sleep = ms => new Promise(r => setTimeout(r, ms));

function serve() {
  return new Promise(res => {
    const s = http.createServer((q, r) => { r.writeHead(200, { 'content-type': 'text/html' }); r.end('<html><body><h1>probe</h1></body></html>'); });
    s.listen(0, '127.0.0.1', () => res({ srv: s, port: s.address().port }));
  });
}
async function start(exe, profile, port, extra = []) {
  fs.mkdirSync(profile, { recursive: true });
  const args = ['--headless=new', '--user-data-dir=' + profile, '--remote-debugging-port=' + port,
    '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
    '--disable-component-update', '--no-sandbox', ...extra, 'about:blank'];
  const c = spawn(exe, args, { stdio: ['ignore', 'pipe', 'pipe'] });
  let err = '';
  c.stderr.on('data', d => { err += d.toString(); });
  let b = null;
  for (let i = 0; i < 60; i++) {
    await sleep(250);
    try { b = await chromium.connectOverCDP('http://127.0.0.1:' + port); break; } catch (e) { }
    if (c.exitCode !== null) break;
  }
  return { child: c, browser: b, stderr: () => err };
}
async function writeState(browser, port, tag) {
  const ctx = browser.contexts()[0];
  const p = await ctx.newPage();
  await p.goto('http://127.0.0.1:' + port + '/');
  await p.evaluate(t => { for (let i = 0; i < 40; i++) localStorage.setItem(t + '-k' + i, (t + '-v' + i).repeat(50)); }, tag);
  await ctx.addCookies([{ name: 'c-' + tag, value: 'x'.repeat(200), domain: '127.0.0.1', path: '/' }]);
  await p.close();
}
async function readState(browser, port) {
  const ctx = browser.contexts()[0];
  const p = await ctx.newPage();
  await p.goto('http://127.0.0.1:' + port + '/');
  const ls = await p.evaluate(() => { const o = []; for (let i = 0; i < localStorage.length; i++) o.push(localStorage.key(i)); return o; });
  const ck = (await ctx.cookies()).map(c => c.name);
  await p.close();
  return { lsKeys: ls.length, lsTags: [...new Set(ls.map(k => k.split('-')[0]))].sort(), cookies: ck.sort() };
}
function sqliteFiles(profile) {
  const out = [];
  const walk = d => {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const f = path.join(d, e.name);
      if (e.isDirectory()) { if (!/Cache|Code Cache|ShaderCache|component_crx_cache|optimization_guide/i.test(e.name)) walk(f); }
      else {
        try {
          const fd = fs.openSync(f, 'r'); const b = Buffer.alloc(16); fs.readSync(fd, b, 0, 16, 0); fs.closeSync(fd);
          if (b.toString('latin1').startsWith('SQLite format 3')) out.push(f);
        } catch (e) { }
      }
    }
  };
  walk(profile); return out;
}
function integrity(file) {
  try {
    const db = new DatabaseSync(file, { readOnly: true });
    const r = db.prepare('PRAGMA integrity_check').all(); db.close();
    return r.map(x => Object.values(x)[0]).join('; ');
  } catch (e) { return 'OPEN FAILED: ' + e.message; }
}
function exitType(profile) {
  try {
    const j = JSON.parse(fs.readFileSync(path.join(profile, 'Default', 'Preferences'), 'utf8'));
    return 'exit_type=' + j.profile.exit_type + ' exited_cleanly=' + j.profile.exited_cleanly;
  } catch (e) { return 'unreadable (' + e.code + ')'; }
}
function report(profile, label) {
  console.log('  [' + label + '] Preferences: ' + exitType(profile));
  const files = sqliteFiles(profile);
  let bad = 0;
  console.log('  [' + label + '] ' + files.length + ' SQLite files under the profile, PRAGMA integrity_check each:');
  for (const f of files) { const r = integrity(f); if (r !== 'ok') bad++; console.log('      ' + path.relative(profile, f).padEnd(46) + r); }
  console.log('  [' + label + '] not-ok: ' + bad + ' of ' + files.length);
  const ldb = [];
  const walk = d => {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const f = path.join(d, e.name);
      if (e.isDirectory()) walk(f); else if (e.name === 'CURRENT' || e.name === 'LOCK') ldb.push(path.relative(profile, path.dirname(f)));
    }
  };
  try { walk(profile); } catch (e) { }
  console.log('  [' + label + '] LevelDB stores present: ' + ([...new Set(ldb)].join(', ') || '(none)'));
}
function countNaming(needle) {
  const cmd = '(Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like ' + "'*" + needle + "*'" + ' }).Count';
  return execFileSync('powershell', ['-NoProfile', '-Command', cmd], { encoding: 'utf8' }).trim();
}
function killTree(pid) {
  try { execFileSync('taskkill', ['/PID', String(pid), '/T', '/F'], { encoding: 'utf8' }); return 'killed'; }
  catch (e) { return 'taskkill: ' + String(e.message).split('\n')[0]; }
}

(async () => {
  const { srv, port } = await serve();
  const ARM = process.argv[2];

  if (ARM === 'kill') {
    const P = path.join(BASE, 'kill-profile');
    fs.rmSync(P, { recursive: true, force: true });
    console.log('=== ARM KILL: full chrome, one profile, whole tree hard-killed (what force_stop_package does) ===');
    const a = await start(FULL, P, 9411);
    console.log('  launched pid=' + a.child.pid + ' CDP=' + !!a.browser);
    await writeState(a.browser, port, 'A');
    console.log('  processes whose command line names this profile, before the kill: ' + countNaming('kill-profile'));
    console.log('  ' + killTree(a.child.pid));
    await sleep(2500);
    console.log('  processes whose command line names this profile, after  the kill: ' + countNaming('kill-profile'));
    report(P, 'after hard kill');
    console.log('  relaunching against the SAME profile directory...');
    const b = await start(FULL, P, 9412);
    console.log('  relaunch pid=' + b.child.pid + ' CDP connected=' + !!b.browser);
    if (b.browser) console.log('  state read back: ' + JSON.stringify(await readState(b.browser, port)));
    console.log('  relaunch stderr: ' + JSON.stringify(b.stderr().split('\n').filter(Boolean).slice(0, 6)));
    try { await b.browser.close(); } catch (e) { }
    killTree(b.child.pid);
    await sleep(1500);
    report(P, 'after a clean close');
  }

  if (ARM === 'concurrent') {
    for (const pair of [['chrome-headless-shell', SHELL], ['full-chrome', FULL]]) {
      const label = pair[0], exe = pair[1];
      const P = path.join(BASE, 'conc-' + label);
      fs.rmSync(P, { recursive: true, force: true });
      console.log('\n=== ARM CONCURRENT (' + label + '): two instances, one profile directory ===');
      const a = await start(exe, P, 9421);
      console.log('  first  pid=' + a.child.pid + ' CDP=' + !!a.browser + ' exitCode=' + a.child.exitCode);
      const b = await start(exe, P, 9422);
      console.log('  second pid=' + b.child.pid + ' CDP=' + !!b.browser + ' exitCode=' + b.child.exitCode);
      console.log('  second stderr: ' + JSON.stringify(b.stderr().split('\n').filter(Boolean).slice(0, 4)));
      if (a.browser && b.browser) {
        for (let round = 0; round < 6; round++) { await writeState(a.browser, port, 'A' + round); await writeState(b.browser, port, 'B' + round); }
        console.log('  both instances wrote 6 interleaved rounds of cookies + localStorage');
      }
      // Close BOTH gracefully, so that what is lost cannot be blamed on a kill.
      for (const h of [a, b]) {
        try { const s = await h.browser.newBrowserCDPSession(); await s.send('Browser.close'); } catch (e) { console.log('  Browser.close said: ' + String(e.message).split('\n')[0]); }
        try { await h.browser.close(); } catch (e) { }
        for (let i = 0; i < 40 && h.child.exitCode === null; i++) await sleep(250);
        console.log('  pid=' + h.child.pid + ' exitCode after graceful close: ' + h.child.exitCode);
        if (h.child.exitCode === null) killTree(h.child.pid);
      }
      await sleep(2500);
      report(P, label + ' after both closed');
      const c = await start(exe, P, 9423);
      console.log('  reopen pid=' + c.child.pid + ' CDP=' + !!c.browser);
      if (c.browser) console.log('  state read back: ' + JSON.stringify(await readState(c.browser, port)));
      try { if (c.browser) await c.browser.close(); } catch (e) { }
      killTree(c.child.pid);
      await sleep(1000);
    }
  }

  if (ARM === 'clean') {
    // CONTROL for the kill arm: same write, but a GRACEFUL shutdown. If the state comes back here
    // and not there, what the hard kill destroyed is the session, not the store.
    const P = path.join(BASE, 'clean-profile');
    fs.rmSync(P, { recursive: true, force: true });
    console.log('=== ARM CLEAN (control): full chrome, one profile, graceful Browser.close() ===');
    const a = await start(FULL, P, 9431);
    console.log('  launched pid=' + a.child.pid + ' CDP=' + !!a.browser);
    await writeState(a.browser, port, 'A');
    console.log('  state written; closing gracefully with the CDP Browser.close command');
    try { const s = await a.browser.newBrowserCDPSession(); await s.send('Browser.close'); } catch (e) { console.log('  Browser.close said: ' + e.message.split('\n')[0]); }
    try { await a.browser.close(); } catch (e) { }
    for (let i = 0; i < 40 && a.child.exitCode === null; i++) await sleep(250);
    console.log('  child exitCode after graceful close: ' + a.child.exitCode);
    console.log('  profile root: ' + fs.readdirSync(P).join(', '));
    console.log('  Default/: ' + fs.readdirSync(path.join(P, 'Default')).filter(n => /Pref|Cookies|Local/i.test(n)).join(', '));
    report(P, 'after graceful close');
    const b = await start(FULL, P, 9432);
    console.log('  relaunch pid=' + b.child.pid + ' CDP=' + !!b.browser);
    if (b.browser) console.log('  state read back: ' + JSON.stringify(await readState(b.browser, port)));
    try { await b.browser.close(); } catch (e) { }
    killTree(b.child.pid);
  }

  if (ARM === 'single-shell') {
    // CONTROL for the concurrent arm: ONE headless shell on the profile, same writes, graceful close.
    const P = path.join(BASE, 'single-shell');
    fs.rmSync(P, { recursive: true, force: true });
    console.log('=== ARM SINGLE-SHELL (control): one chrome-headless-shell, one profile ===');
    const a = await start(SHELL, P, 9441);
    console.log('  launched pid=' + a.child.pid + ' CDP=' + !!a.browser);
    for (let round = 0; round < 6; round++) await writeState(a.browser, port, 'A' + round);
    try { const s = await a.browser.newBrowserCDPSession(); await s.send('Browser.close'); } catch (e) { }
    try { await a.browser.close(); } catch (e) { }
    for (let i = 0; i < 40 && a.child.exitCode === null; i++) await sleep(250);
    console.log('  exitCode after graceful close: ' + a.child.exitCode);
    report(P, 'single shell');
    const b = await start(SHELL, P, 9442);
    console.log('  reopen pid=' + b.child.pid + ' CDP=' + !!b.browser);
    if (b.browser) console.log('  state read back: ' + JSON.stringify(await readState(b.browser, port)));
    try { const s = await b.browser.newBrowserCDPSession(); await s.send('Browser.close'); } catch (e) { }
    try { await b.browser.close(); } catch (e) { }
    await sleep(1500); killTree(b.child.pid);
  }

  if (ARM === 'control') {
    const src = process.argv[3];
    const dst = path.join(BASE, 'control-corrupt.sqlite');
    fs.copyFileSync(src, dst);
    console.log('  POSITIVE CONTROL -- copy of ' + path.basename(src));
    console.log('    before doctoring                        : ' + integrity(dst));
    const fd = fs.openSync(dst, 'r+');
    const size = fs.fstatSync(fd).size;
    fs.writeSync(fd, Buffer.alloc(1024, 0x5a), 0, 1024, Math.floor(size / 2));
    fs.closeSync(fd);
    console.log('    after 1024 bytes of 0x5A written mid-file: ' + integrity(dst).slice(0, 400));
  }
  srv.close();
})().catch(e => { console.error('PROBE FAILED: ' + e.stack); process.exit(1); });
