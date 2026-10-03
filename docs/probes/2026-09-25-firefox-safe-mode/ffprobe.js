// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch rig (Q302): one headless Firefox launch through the payload's
// playwright-core, with the product's launch options, timed and logged.
// Usage: node ffprobe.js <path-to-args.json>
'use strict';
const fs = require('fs');
const path = require('path');
const http = require('http');
const cp = require('child_process');

const args = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const PW = args.playwrightCore;
const t0 = process.hrtime.bigint();
const ms = () => Number(process.hrtime.bigint() - t0) / 1e6;
const logFd = fs.openSync(args.log, 'a');
const w = (s) => { try { fs.writeSync(logFd, `${new Date().toISOString()} +${ms().toFixed(1)} ${s}\n`); } catch {} };
process.on('uncaughtException', (e) => { w(`UNCAUGHT ${e && e.stack || e}`); });
process.on('unhandledRejection', (e) => { w(`UNHANDLED ${e && e.stack || e}`); });

// Tee the browser's own stdout/stderr with timestamps. Playwright reads the
// same streams through readline; a second 'data' listener only observes.
let firefoxPid = 0;
const origSpawn = cp.spawn;
cp.spawn = function (command, argv, options) {
  const child = origSpawn.apply(this, arguments);
  if (/firefox\.exe$/i.test(String(command))) {
    firefoxPid = child.pid || 0;
    w(`SPAWN pid=${firefoxPid} ${command} ${(argv || []).join(' ')}`);
    const tee = (name) => (chunk) => {
      for (const line of String(chunk).split(/\r?\n/)) if (line.length) w(`[ff:${name}] ${line}`);
    };
    child.stdout && child.stdout.on('data', tee('out'));
    child.stderr && child.stderr.on('data', tee('err'));
    child.on('exit', (code, signal) => w(`FIREFOX-EXIT code=${code} signal=${signal}`));
  }
  return child;
};

const utils = require(path.join(PW, 'lib', 'utilsBundle'));
utils.debug.log = (...a) => w('[dbg] ' + a.join(' ').replace(/\u001b\[[0-9;]*m/g, ''));
if (args.debug) utils.debug.enable(args.debug);
const { firefox } = require(PW);

function listProfile(dir) {
  const files = [];
  const walk = (d) => {
    let entries = [];
    try { entries = fs.readdirSync(d, { withFileTypes: true }); } catch { return; }
    for (const e of entries) {
      const p = path.join(d, e.name);
      if (e.isDirectory()) walk(p);
      else { try { const st = fs.statSync(p); files.push({ p: path.relative(dir, p), size: st.size, mtime: st.mtime.toISOString(), birth: st.birthtime.toISOString() }); } catch { files.push({ p: path.relative(dir, p), size: -1 }); } }
    }
  };
  walk(dir);
  return files;
}

(async () => {
  const server = http.createServer((req, res) => {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
    res.end('<!doctype html><html><head><title>probe</title></head><body><p>hello</p></body></html>');
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  const url = `http://127.0.0.1:${server.address().port}/`;
  fs.mkdirSync(args.profile, { recursive: true });
  fs.mkdirSync(args.downloads, { recursive: true });
  fs.mkdirSync(args.artifacts, { recursive: true });
  w(`START node=${process.version} pid=${process.pid} id=${args.id}`);

  const result = { id: args.id, batch: args.batch, mode: args.mode, nodePid: process.pid, startUtc: new Date().toISOString() };
  let suspectFired = false;
  const suspect = setTimeout(() => {
    suspectFired = true;
    const files = listProfile(args.profile);
    w(`SUSPECT no context after ${args.suspectMs} ms; firefoxPid=${firefoxPid}; profile files=${files.length} bytes=${files.reduce((a, f) => a + Math.max(0, f.size), 0)}`);
    fs.writeFileSync(args.log.replace(/\.log$/, '.suspect-profile.json'), JSON.stringify(files, null, 1));
    // Tell the orchestrator which pid to inspect.
    fs.writeFileSync(args.log.replace(/\.log$/, '.suspect'), String(firefoxPid));
  }, args.suspectMs);

  const tl = ms();
  let context;
  try {
    context = await firefox.launchPersistentContext(args.profile, {
      executablePath: args.exe,
      headless: true,
      firefoxUserPrefs: { 'toolkit.winRegisterApplicationRestart': false, 'signon.rememberSignons': false },
      downloadsPath: args.downloads,
      artifactsDir: args.artifacts,
      viewport: { width: 1920, height: 1080 },
      locale: 'nl-NL',
      timezoneId: 'Europe/Amsterdam',
      ignoreHTTPSErrors: false,
      handleSIGINT: false,
      handleSIGTERM: false,
      ignoreDefaultArgs: ['--disable-extensions'],
      timeout: args.timeoutMs,
    });
    result.launchMs = +(ms() - tl).toFixed(1);
    w(`CONTEXT after ${result.launchMs} ms`);
    const page = context.pages()[0] || await context.newPage();
    const tn = ms();
    await page.goto(url, { timeout: args.timeoutMs });
    result.navMs = +(ms() - tn).toFixed(1);
    result.title = await page.title();
    w(`NAVIGATED after ${result.navMs} ms title=${result.title}`);
    result.ok = true;
  } catch (e) {
    result.ok = false;
    result.failMs = +(ms() - tl).toFixed(1);
    result.error = String(e && e.message || e).slice(0, 6000);
    w(`FAILED after ${result.failMs} ms: ${result.error}`);
  }
  clearTimeout(suspect);
  result.suspect = suspectFired;
  result.firefoxPid = firefoxPid;
  const files = listProfile(args.profile);
  result.profileFiles = files.length;
  result.profileBytes = files.reduce((a, f) => a + Math.max(0, f.size), 0);
  try { fs.writeFileSync(args.log.replace(/\.log$/, '.profile-at-end.json'), JSON.stringify(files, null, 1)); } catch {}
  try { result.checkpoints = fs.readFileSync(path.join(args.profile, 'sessionCheckpoints.json'), 'utf8'); } catch { result.checkpoints = null; }
  result.startupIncomplete = fs.existsSync(path.join(args.profile, '.startup-incomplete'));
  if (context) {
    const tc = ms();
    try {
      await Promise.race([context.close(), new Promise((_, r) => setTimeout(() => r(new Error('close bound')), 60000))]);
      result.closeMs = +(ms() - tc).toFixed(1);
    } catch (e) { result.closeError = String(e.message || e); }
  }
  server.close();
  fs.appendFileSync(args.results, JSON.stringify(result) + '\n');
  w(`DONE ${JSON.stringify(result).slice(0, 400)}`);
  fs.closeSync(logFd);
  process.exit(0);
})();
