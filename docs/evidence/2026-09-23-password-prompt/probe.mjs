// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Reproduces the "do you want to save this password" prompt the way BrowserAI
// launches a browser, and measures whether it appeared -- by enumerating the
// windows owned by the browser's own pid tree, never by image name.
//
// Usage:
//   node probe.mjs --browser chromium|firefox --arm <name> [--prefs] [--switch] [--seed-preferences]
//
// Every arm gets a fresh scratch user-data-dir under .work/password-2026-09-23/.

import { createServer } from 'node:http';
import { mkdirSync, writeFileSync, readFileSync, existsSync, rmSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import path from 'node:path';

const HERE = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const PW = path.join('C:', 'Source', 'SixFive7', 'BrowserAI', 'payload', 'mcp', 'node_modules', 'playwright-core');
const BROWSERS_ROOT = path.join(process.env.LOCALAPPDATA, 'BrowserAI', 'browsers');

process.env.PLAYWRIGHT_BROWSERS_PATH = BROWSERS_ROOT;

const argv = process.argv.slice(2);
const flag = (n) => argv.includes('--' + n);
const val = (n, d) => { const i = argv.indexOf('--' + n); return i >= 0 ? argv[i + 1] : d; };

const browserName = val('browser', 'chromium');
const arm = val('arm', 'default');
const useFirefoxPrefs = flag('prefs');
const useSwitch = flag('switch');
const seedPreferences = flag('seed-preferences');
const holdMs = Number(val('hold', '6000'));

const outDir = path.join(HERE, 'out', `${browserName}-${arm}`);
rmSync(outDir, { recursive: true, force: true });
mkdirSync(outDir, { recursive: true });
const profileDir = path.join(outDir, 'profile');
mkdirSync(profileDir, { recursive: true });

const log = [];
const say = (...a) => { const line = a.join(' '); log.push(line); console.log(line); };

// ---------------------------------------------------------------- local page
const PAGE = `<!doctype html><html><head><meta charset=utf-8><title>BrowserAI password probe</title></head>
<body><h1>probe login</h1>
<form method="POST" action="/login" id="f">
  <label>Username <input type="text" name="username" id="username" autocomplete="username"></label>
  <label>Password <input type="password" name="password" id="password" autocomplete="current-password"></label>
  <button type="submit" id="go">Sign in</button>
</form></body></html>`;

const DONE = `<!doctype html><html><head><meta charset=utf-8><title>signed in</title></head>
<body><h1>welcome</h1><p id="ok">signed in</p></body></html>`;

const server = createServer((req, res) => {
  if (req.method === 'POST' && req.url === '/login') {
    let body = '';
    req.on('data', (c) => { body += c; });
    req.on('end', () => {
      say(`[server] POST /login body=${body}`);
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
      res.end(DONE);
    });
    return;
  }
  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
  res.end(PAGE);
});

await new Promise((r) => server.listen(0, '127.0.0.1', r));
const port = server.address().port;
const origin = `http://127.0.0.1:${port}`;
say(`[probe] arm=${browserName}/${arm} origin=${origin} profile=${profileDir}`);
say(`[probe] prefs=${useFirefoxPrefs} switch=${useSwitch} seedPreferences=${seedPreferences}`);

// ------------------------------------------------- optional seeded Preferences
if (seedPreferences) {
  const defaultDir = path.join(profileDir, 'Default');
  mkdirSync(defaultDir, { recursive: true });
  const prefs = JSON.parse(val('seed-json', '{"credentials_enable_service":false,"profile":{"password_manager_enabled":false}}'));
  writeFileSync(path.join(defaultDir, 'Preferences'), JSON.stringify(prefs), 'utf8');
  say(`[probe] seeded ${path.join(defaultDir, 'Preferences')} = ${JSON.stringify(prefs)}`);
}

// ------------------------------------------------------------------- launch
const pw = await import(new URL('file:///' + path.join(PW, 'index.mjs').replace(/\\/g, '/')).href);
const { chromium, firefox } = pw.default ?? pw;
const type = browserName === 'firefox' ? firefox : chromium;

const launchOptions = {
  headless: flag('headless'),
  downloadsPath: path.join(outDir, 'downloads'),
  viewport: { width: 1280, height: 800 },
  locale: 'en-US',
  ignoreHTTPSErrors: true,
};

if (browserName === 'firefox') {
  launchOptions.firefoxUserPrefs = { 'toolkit.winRegisterApplicationRestart': false };
  if (useFirefoxPrefs) {
    Object.assign(launchOptions.firefoxUserPrefs, {
      'signon.rememberSignons': false,
    });
  }
  // The positive control: put the pref back the way an un-suppressed Firefox
  // has it, so "no prompt" in the baseline is a measurement and not a probe
  // that cannot see one.
  if (flag('firefox-remember')) {
    Object.assign(launchOptions.firefoxUserPrefs, {
      'signon.rememberSignons': true,
    });
  }
} else {
  launchOptions.channel = 'chrome-for-testing';
  launchOptions.permissions = ['clipboard-read'];
  if (useSwitch) {
    launchOptions.args = ['--disable-features=PasswordManagerEnableReceiverService,AutofillEnableAccountStorageForServerCardSaveAndFill,PasswordLeakDetection'];
  }
  const extraSwitch = val('extra-switch', null);
  if (extraSwitch) {
    launchOptions.args = (launchOptions.args ?? []).concat(extraSwitch.split('|'));
  }
}

say(`[probe] launchOptions=${JSON.stringify(launchOptions)}`);

const context = await type.launchPersistentContext(profileDir, launchOptions);

// the browser's own pid, asked of Playwright, never of an image name
let browserPid = null;
try { browserPid = context.browser()?.process()?.pid ?? null; } catch { /* ignore */ }
if (!browserPid) {
  // Fall back to the node process: the browser is a Win32 child of it, and the
  // enumerator walks ParentProcessId from whatever root it is given.
  browserPid = process.pid;
  say('[probe] context.browser() gave no pid; rooting the pid walk at node itself');
}
say(`[probe] pid walk root = ${browserPid} (node pid ${process.pid})`);

const enumerate = (label) => {
  const file = path.join(outDir, `windows-${label}.json`);
  const stdout = execFileSync('pwsh', [
    '-NoProfile', '-ExecutionPolicy', 'Bypass',
    '-File', path.join(HERE, 'windows-of.ps1'),
    '-RootPid', String(browserPid),
    '-Label', label,
    '-OutFile', file,
  ], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  return JSON.parse(stdout);
};

const shoot = (label) => {
  const file = path.join(outDir, `screen-${label}.png`);
  try {
    execFileSync('pwsh', [
      '-NoProfile', '-ExecutionPolicy', 'Bypass',
      '-File', path.join(HERE, 'shoot.ps1'),
      '-OutFile', file,
    ], { encoding: 'utf8' });
    say(`[probe] screenshot ${file}`);
  } catch (e) {
    say(`[probe] screenshot failed: ${e.message}`);
  }
};

const page = context.pages()[0] ?? await context.newPage();
await page.goto(origin, { waitUntil: 'load' });
await page.waitForTimeout(1500);

const fingerprint = await page.evaluate(() => JSON.stringify({
  webdriver: navigator.webdriver,
  ua: navigator.userAgent,
}));
say(`[probe] fingerprint ${fingerprint}`);

const before = enumerate('before');
say(`[probe] before: ${before.windows.length} windows in pid tree ${before.pidsInTree.join(',')}`);
shoot('before');

await page.fill('#username', 'probe-user');
await page.fill('#password', 'Sup3rSecret!probe');
await page.click('#go');
await page.waitForSelector('#ok', { timeout: 30000 });
say('[probe] navigation after submit completed (#ok present)');

// Poll for a new window for up to `holdMs`, so the wait is a bound and not a
// promptness claim: the first poll that shows a new window stops the loop.
const beforeKeys = new Set(before.windows.map((w) => `${w.class}|${w.title}|${w.rect}`));
let after = null;
let firstSeenMs = null;
const started = Date.now();
while (Date.now() - started < holdMs) {
  await page.waitForTimeout(500);
  after = enumerate('after');
  const fresh = after.windows.filter((w) => !beforeKeys.has(`${w.class}|${w.title}|${w.rect}`));
  if (fresh.length > 0 && firstSeenMs === null) {
    firstSeenMs = Date.now() - started;
    say(`[probe] new windows first seen after ${firstSeenMs} ms`);
  }
}
after ??= enumerate('after');
shoot('after');

const fresh = after.windows.filter((w) => !beforeKeys.has(`${w.class}|${w.title}|${w.rect}`));
say(`[probe] after: ${after.windows.length} windows; ${fresh.length} not present before`);
for (const w of fresh) {
  say(`[probe]   NEW ${w.level} ${w.hwnd} pid=${w.pid} visible=${w.visible} class='${w.class}' title='${w.title}' rect=${w.rect}`);
  if (!w.visible) continue;
  const safe = (w.title || w.class).replace(/[^A-Za-z0-9._-]+/g, '_').slice(0, 60);
  try {
    const r = execFileSync('pwsh', [
      '-NoProfile', '-ExecutionPolicy', 'Bypass',
      '-File', path.join(HERE, 'shoot-window.ps1'),
      '-Hwnd', w.hwnd.replace(/^0x/, ''),
      '-OutFile', path.join(outDir, `win-${safe}.png`),
    ], { encoding: 'utf8' });
    say(`[probe]     ${r.trim()}`);
  } catch (e) {
    say(`[probe]     window capture failed: ${e.message.split('\n')[0]}`);
  }
}

// Full listing, both snapshots, for the record.
writeFileSync(path.join(outDir, 'before.txt'), before.windows.map((w) => `${w.level} ${w.hwnd} pid=${w.pid} vis=${w.visible} class='${w.class}' title='${w.title}' rect=${w.rect}`).join('\n'), 'utf8');
writeFileSync(path.join(outDir, 'after.txt'), after.windows.map((w) => `${w.level} ${w.hwnd} pid=${w.pid} vis=${w.visible} class='${w.class}' title='${w.title}' rect=${w.rect}`).join('\n'), 'utf8');

// Page-side screenshot too, so the content half is on the record.
await page.screenshot({ path: path.join(outDir, 'page-after.png') });

await context.close();
server.close();

// After the browser has exited: what did Chromium do to a seeded Preferences file?
const prefsPath = path.join(profileDir, 'Default', 'Preferences');
if (existsSync(prefsPath)) {
  const text = readFileSync(prefsPath, 'utf8');
  writeFileSync(path.join(outDir, 'Preferences-after.json'), text, 'utf8');
  let parsed = null;
  try { parsed = JSON.parse(text); } catch { /* ignore */ }
  say(`[probe] Preferences after launch: ${text.length} bytes; credentials_enable_service=${JSON.stringify(parsed?.credentials_enable_service)} profile.password_manager_enabled=${JSON.stringify(parsed?.profile?.password_manager_enabled)}`);
} else {
  say('[probe] no Default/Preferences after launch');
}

// Firefox: did it store a login?
for (const name of ['logins.json', 'prefs.js', 'user.js']) {
  const p = path.join(profileDir, name);
  if (existsSync(p)) {
    const text = readFileSync(p, 'utf8');
    if (name === 'logins.json') say(`[probe] logins.json: ${text.slice(0, 400)}`);
    if (name !== 'logins.json') {
      const hit = text.split('\n').filter((l) => /signon|password/i.test(l));
      if (hit.length) say(`[probe] ${name} signon/password lines: ${hit.join(' ~ ')}`);
    }
  }
}

writeFileSync(path.join(outDir, 'probe.log'), log.join('\n'), 'utf8');
console.log(`\n[probe] VERDICT newWindows=${fresh.length} firstSeenMs=${firstSeenMs} outDir=${outDir}`);
process.exit(0);
