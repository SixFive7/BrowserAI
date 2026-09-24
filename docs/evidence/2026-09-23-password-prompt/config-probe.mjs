// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The same question as probe.mjs, asked through the product's own path: the
// payload's `cli.js`, driven over stdio with a config shaped exactly like the
// one `BrowserConfiguration.Generate` writes, plus one candidate suppression.
//
//   node config-probe.mjs <chromium|firefox> <arm> [--args <switch>] [--pref <name>=<true|false>]
//
// It answers three things a raw playwright-core launch cannot:
//   1. does the key survive `loadConfig`'s bare JSON.parse and reach the browser,
//   2. does `browser_get_config` read it back (BrowserAI's round-trip doctrine),
//   3. is the prompt gone, measured by the same pid-keyed window enumeration.

import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { createServer } from 'node:http';
import path from 'node:path';

const HERE = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const REPO = path.join('C:', 'Source', 'SixFive7', 'BrowserAI');
const nodeExe = path.join(REPO, 'payload', 'node', 'node.exe');
const cli = path.join(REPO, 'payload', 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js');

const browserName = process.argv[2] ?? 'chromium';
const arm = process.argv[3] ?? 'default';
const rest = process.argv.slice(4);
const take = (n) => { const i = rest.indexOf(n); return i >= 0 ? rest[i + 1] : null; };
const extraArg = take('--args');
const prefPair = take('--pref');
const headless = rest.includes('--headless');

const root = path.join(HERE, 'cfg', `${browserName}-${arm}`);
rmSync(root, { recursive: true, force: true });
mkdirSync(root, { recursive: true });
const outputDir = path.join(root, 'output');
mkdirSync(outputDir, { recursive: true });

const log = [];
const say = (...a) => { const l = a.join(' '); log.push(l); console.log(l); };

const PAGE = `<!doctype html><html><head><meta charset=utf-8><title>BrowserAI password probe</title></head>
<body><h1>probe login</h1>
<form method="POST" action="/login" id="f">
  <label>Username <input type="text" name="username" id="username" autocomplete="username"></label>
  <label>Password <input type="password" name="password" id="password" autocomplete="current-password"></label>
  <button type="submit" id="go">Sign in</button>
</form></body></html>`;
const DONE = `<!doctype html><html><head><meta charset=utf-8><title>signed in</title></head><body><h1>welcome</h1><p id="ok">signed in</p></body></html>`;

let posted = false;
const server = createServer((req, res) => {
  if (req.method === 'POST' && req.url === '/login') {
    let body = '';
    req.on('data', (c) => { body += c; });
    req.on('end', () => { posted = true; say(`[server] POST /login ${body}`); res.writeHead(200, { 'content-type': 'text/html' }); res.end(DONE); });
    return;
  }
  res.writeHead(200, { 'content-type': 'text/html' });
  res.end(PAGE);
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const origin = `http://127.0.0.1:${server.address().port}/`;

// Shaped after BrowserConfiguration.Generate, key for key.
const launchOptions = {
  headless,
  downloadsPath: path.join(root, 'downloads'),
};
if (browserName === 'firefox') {
  launchOptions.firefoxUserPrefs = { 'toolkit.winRegisterApplicationRestart': false };
  if (prefPair) {
    const [name, value] = prefPair.split('=');
    launchOptions.firefoxUserPrefs[name] = value === 'true';
  }
} else {
  launchOptions.channel = 'chrome-for-testing';
  if (extraArg) launchOptions.args = extraArg.split('|');
}

const config = {
  browser: {
    browserName,
    userDataDir: path.join(root, 'profile'),
    launchOptions,
    contextOptions: {
      viewport: { width: 1280, height: 800 },
      locale: 'en-US',
      ignoreHTTPSErrors: true,
      ...(browserName === 'firefox' ? {} : { permissions: ['clipboard-read'] }),
    },
  },
  capabilities: ['config', 'vision', 'devtools', 'storage', 'network', 'pdf', 'testing'],
  outputDir,
  saveSession: false,
  allowUnrestrictedFileAccess: false,
  console: { level: 'debug' },
  snapshot: { boxes: true },
  codegen: 'none',
  filePaths: 'absolute',
  timeouts: { idle: 3600000 },
  webmcp: true,
};
const configPath = path.join(root, 'config.json');
writeFileSync(configPath, JSON.stringify(config, null, 2), 'utf8');
say(`[cfg] ${configPath}`);
say(`[cfg] launchOptions=${JSON.stringify(launchOptions)}`);

const environment = {};
for (const [k, v] of Object.entries(process.env)) {
  if (/^(PLAYWRIGHT_MCP|DEBUG|NODE_OPTIONS|NODE_PATH)/i.test(k)) continue;
  environment[k] = v;
}
environment.PLAYWRIGHT_BROWSERS_PATH = path.join(process.env.LOCALAPPDATA, 'BrowserAI', 'browsers');

const child = spawn(nodeExe, [cli, '--config', configPath, '--sandbox'], {
  cwd: root, env: environment, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true,
});
say(`[cfg] child node pid ${child.pid}`);

const waiting = new Map();
let pendingText = '';
let stderr = '';
let nextId = 1;
child.stdout.setEncoding('utf8');
child.stderr.setEncoding('utf8');
child.stderr.on('data', (c) => { stderr += c; });
child.stdout.on('data', (chunk) => {
  pendingText += chunk;
  for (let end = pendingText.indexOf('\n'); end >= 0; end = pendingText.indexOf('\n')) {
    const line = pendingText.slice(0, end).trim();
    pendingText = pendingText.slice(end + 1);
    if (!line) continue;
    let m; try { m = JSON.parse(line); } catch { continue; }
    if (m.id === undefined) continue;
    if (waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
  }
});

const request = (method, params) => {
  const id = nextId++;
  return new Promise((ok, fail) => {
    const guard = setTimeout(() => fail(new Error(`${method} timed out. stderr: ${stderr}`)), 180_000);
    waiting.set(id, (m) => { clearTimeout(guard); ok(m); });
    child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
  });
};
const call = (name, args) => request('tools/call', { name, arguments: args ?? {} });
const textOf = (m) => (m.result?.content ?? []).filter((b) => b.type === 'text').map((b) => b.text).join('\n');

await request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'password-probe', version: '1' } });
child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' })}\n`);

const nav = await call('browser_navigate', { url: origin });
say(`[cfg] navigate -> ${textOf(nav).slice(0, 200).replace(/\n/g, ' | ')}`);

const enumerate = (label) => {
  const file = path.join(root, `windows-${label}.json`);
  const stdout = execFileSync('pwsh', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(HERE, 'windows-of.ps1'),
    '-RootPid', String(child.pid), '-Label', label, '-OutFile', file], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  return JSON.parse(stdout);
};

const before = enumerate('before');
say(`[cfg] before: ${before.windows.length} windows, ${before.pidsInTree.length} pids`);
const browserCmd = (before.commandLines ?? []).find((c) => (c.commandLine ?? '').includes('--user-data-dir') || (c.commandLine ?? '').includes('-profile '));
say(`[cfg] browser command line (pid ${browserCmd?.pid}): ${(browserCmd?.commandLine ?? '(not found)').slice(0, 1200)}`);

const snap = await call('browser_snapshot', {});
const snapText = textOf(snap);
writeFileSync(path.join(root, 'snapshot.txt'), snapText, 'utf8');
const refOf = (needle) => {
  const line = snapText.split('\n').find((l) => l.includes(needle) && l.includes('[ref='));
  return line ? /\[ref=([^\]]+)\]/.exec(line)?.[1] : null;
};
const userRef = refOf('Username');
const passRef = refOf('Password');
const goRef = refOf('Sign in');
say(`[cfg] refs username=${userRef} password=${passRef} go=${goRef}`);

await call('browser_type', { element: 'Username', target: userRef, text: 'probe-user' });
await call('browser_type', { element: 'Password', target: passRef, text: 'Sup3rSecret!probe' });
const clicked = await call('browser_click', { element: 'Sign in', target: goRef });
say(`[cfg] click -> ${textOf(clicked).split('\n').slice(0, 3).join(' | ')}`);

const beforeKeys = new Set(before.windows.filter((w) => w.level === 'top').map((w) => w.hwnd));
let after = null;
const started = Date.now();
while (Date.now() - started < 8000) {
  await new Promise((r) => setTimeout(r, 700));
  after = enumerate('after');
}
const fresh = after.windows.filter((w) => w.level === 'top' && !beforeKeys.has(w.hwnd));
say(`[cfg] after: ${after.windows.length} windows; ${fresh.length} new`);
let promptSeen = false;
for (const w of fresh) {
  say(`[cfg]   NEW ${w.level} ${w.hwnd} pid=${w.pid} visible=${w.visible} class='${w.class}' title='${w.title}' rect=${w.rect}`);
  if (w.class === 'Chrome_WidgetWin_1' && w.title === 'Save password?') promptSeen = true;
  if (w.class === 'MozillaDropShadowWindowClass' && w.level === 'top') promptSeen = true;
  if (!w.visible) continue;
  const safe = (w.title || w.class).replace(/[^A-Za-z0-9._-]+/g, '_').slice(0, 60);
  try {
    say('[cfg]     ' + execFileSync('pwsh', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(HERE, 'shoot-window.ps1'),
      '-Hwnd', w.hwnd.replace(/^0x/, ''), '-OutFile', path.join(root, `win-${safe}.png`)], { encoding: 'utf8' }).trim());
  } catch (e) { say(`[cfg]     capture failed ${e.message.split('\n')[0]}`); }
}

const resolved = await call('browser_get_config', {});
const resolvedText = textOf(resolved);
writeFileSync(path.join(root, 'resolved-config.txt'), resolvedText, 'utf8');
const brace = resolvedText.indexOf('{');
let echoed = null;
try { echoed = JSON.parse(resolvedText.slice(brace)); } catch { /* leave null */ }
say(`[cfg] browser_get_config launchOptions = ${JSON.stringify(echoed?.browser?.launchOptions)}`);

await call('browser_close', {});
child.stdin.end();
server.close();
writeFileSync(path.join(root, 'probe.log'), log.join('\n'), 'utf8');
console.log(`\n[cfg] VERDICT promptSeen=${promptSeen} newWindows=${fresh.length} root=${root}`);
setTimeout(() => process.exit(0), 2000);
