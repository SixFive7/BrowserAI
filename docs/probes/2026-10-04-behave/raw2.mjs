// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The second pass over the two ways of giving a hidden Chromium the headed user
// agent, with BrowserAI's own launch options around them: whether the pages a
// session restore reopens at launch carry the override from their first
// request, and whether a service worker's requests do. The payload's own cli.js
// driven over stdio with a hand-written config per arm, no BrowserAI process on
// the path. Run through HiddenDesktop.ps1 like raw.mjs.
//
//   node raw2.mjs payload=<payload dir> out=<dir>
//
// First it asks the provisioned chrome.exe itself for its user agent with
// --headless --dump-dom, the derivation the product would use, and times it.
// Then one child per arm: nothing set, the --user-agent switch, and Playwright's
// contextOptions.userAgent, headless and headed.

import { mkdirSync, writeFileSync, rmSync, appendFileSync, createWriteStream, readdirSync } from 'node:fs';
import { spawn, spawnSync } from 'node:child_process';
import path from 'node:path';
import { startSite } from './site.mjs';

const opt = Object.fromEntries(process.argv.slice(2).map((a) => { const i = a.indexOf('='); return [a.slice(0, i), a.slice(i + 1)]; }));
const OUT = opt.out;
rmSync(OUT, { recursive: true, force: true });
mkdirSync(OUT, { recursive: true });
const NODE = path.join(opt.payload, 'node', 'node.exe');
const CLI = path.join(opt.payload, 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js');
const BROWSERS = path.join(process.env.LOCALAPPDATA, 'BrowserAI', 'browsers');
const chromiumDir = readdirSync(BROWSERS).filter((d) => /^chromium-\d+$/.test(d)).sort().at(-1);
const CHROME = path.join(BROWSERS, chromiumDir, 'chrome-win64', 'chrome.exe');
const log = (...a) => appendFileSync(path.join(OUT, 'raw.log'), [new Date().toISOString(), ...a].join(' ') + '\n');
const results = { chrome: CHROME, startedUtc: new Date().toISOString(), probe: [], arms: [] };
const save = () => writeFileSync(path.join(OUT, 'results.json'), JSON.stringify(results, null, 2));

// The derivation: the browser's own headless user agent, read off a page it
// rendered, with a profile of the rig's own so nothing touches another one.
for (let i = 0; i < 3; i++) {
  const profile = path.join(OUT, `probe-profile-${i}`);
  const started = Date.now();
  const r = spawnSync(CHROME, ['--headless', '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
    '--disable-component-update', '--disable-sync', `--user-data-dir=${profile}`, '--dump-dom',
    'data:text/html,<title>ua</title><script>document.write(navigator.userAgent)</script>'], { encoding: 'utf8', windowsHide: true, timeout: 60000 });
  const body = (r.stdout ?? '').match(/<body>([^<]*)<\/body>/)?.[1] ?? null;
  results.probe.push({ ms: Date.now() - started, status: r.status, signal: r.signal, stdoutBytes: (r.stdout ?? '').length, stderrHead: (r.stderr ?? '').slice(0, 400), userAgent: body });
  log('probe', i, JSON.stringify(results.probe.at(-1)));
  save();
}
const headless = results.probe.find((p) => p.userAgent)?.userAgent ?? null;
const derived = headless ? headless.replace('HeadlessChrome/', 'Chrome/') : null;
results.derived = derived;
save();

const site = await startSite('raw');
results.origin = site.origin;

const READ_UA = `async () => {
  const out = { ua: navigator.userAgent, webdriver: navigator.webdriver };
  try {
    const d = navigator.userAgentData;
    out.brands = d ? d.brands.map((b) => b.brand + '/' + b.version).join(', ') : null;
    out.mobile = d ? d.mobile : null;
    out.platform = d ? d.platform : null;
    if (d) {
      const high = await d.getHighEntropyValues(['fullVersionList', 'platformVersion', 'architecture', 'bitness', 'model']);
      out.fullVersionList = (high.fullVersionList || []).map((b) => b.brand + '/' + b.version).join(', ');
      out.platformVersion = high.platformVersion; out.architecture = high.architecture; out.bitness = high.bitness;
    }
  } catch (e) { out.uaDataError = String(e); }
  try { out.fetched = await (await fetch('/headers?from=page', { cache: 'no-store' })).json(); } catch (e) { out.fetched = 'THREW ' + e.message; }
  try {
    out.worker = await new Promise((resolve) => {
      const w = new Worker('/worker.js');
      const t = setTimeout(() => resolve('no answer in 10 s'), 10000);
      w.onmessage = (m) => { clearTimeout(t); resolve(m.data); w.terminate(); };
      w.onerror = (e) => { clearTimeout(t); resolve('worker error ' + e.message); };
    });
  } catch (e) { out.worker = 'THREW ' + e.message; }
  return JSON.stringify(out);
}`;

const ARMS = [
  { name: 'none-headless', headless: true },
  { name: 'context-headless', headless: true, contextOptions: derived ? { userAgent: derived } : {} },
  { name: 'switch-and-context-headless', headless: true, args: derived ? [`--user-agent=${derived}`] : [], contextOptions: derived ? { userAgent: derived } : {} },
  { name: 'none-headed', headless: false },
];

function client(configPath, dir) {
  const env = {};
  for (const [k, v] of Object.entries(process.env)) { if (!/^(PLAYWRIGHT_MCP|DEBUG|NODE_OPTIONS|NODE_PATH)/i.test(k)) env[k] = v; }
  Object.assign(env, { PLAYWRIGHT_BROWSERS_PATH: BROWSERS, PLAYWRIGHT_SKIP_BROWSER_GC: '1', PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1' });
  const child = spawn(NODE, [CLI, '--config', configPath], { cwd: dir, env, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  child.stderr.pipe(createWriteStream(path.join(dir, 'child.stderr.log')));
  const waiting = new Map();
  let next = 1;
  let pending = '';
  child.stdout.setEncoding('utf8');
  child.stdout.on('data', (chunk) => {
    pending += chunk;
    for (let end = pending.indexOf('\n'); end >= 0; end = pending.indexOf('\n')) {
      const line = pending.slice(0, end).trim();
      pending = pending.slice(end + 1);
      if (!line) continue;
      let m; try { m = JSON.parse(line); } catch { continue; }
      if (m.id !== undefined && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    }
  });
  const rpc = (method, params, ms = 180000) => new Promise((resolve, reject) => {
    const id = next++;
    const t = setTimeout(() => { waiting.delete(id); reject(new Error(`${method} timed out`)); }, ms);
    waiting.set(id, (m) => { clearTimeout(t); resolve(m); });
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  });
  const call = async (name, args) => {
    const m = await rpc('tools/call', { name, arguments: args });
    return { isError: !!m.result?.isError, text: (m.result?.content ?? []).filter((c) => c.type === 'text').map((c) => c.text).join('\n') };
  };
  return { child, rpc, call, notify: (method) => child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method }) + '\n') };
}

for (const arm of ARMS) {
  const dir = path.join(OUT, arm.name);
  mkdirSync(path.join(dir, 'output'), { recursive: true });
  const config = {
    browser: {
      browserName: 'chromium',
      userDataDir: path.join(dir, 'profile'),
      launchOptions: {
        channel: 'chrome-for-testing',
        headless: arm.headless,
        // BrowserAI's own switches, so the arm differs from a session only in the user agent.
        args: ['--enable-automation', '--disable-blink-features=AutomationControlled', '--restore-last-session', '--enable-aggressive-domstorage-flushing', '--hide-crash-restore-bubble', ...(arm.args ?? [])],
        ignoreDefaultArgs: ['about:blank'],
      },
      contextOptions: { viewport: { width: 1920, height: 1080 }, ...(arm.contextOptions ?? {}) },
    },
    outputDir: path.join(dir, 'output'),
  };
  const configPath = path.join(dir, 'config.json');
  writeFileSync(configPath, JSON.stringify(config, null, 2));
  const c = client(configPath, dir);
  const row = { arm: arm.name, config: config.browser };
  const hitsBefore = site.hits.length;
  try {
    await c.rpc('initialize', { protocolVersion: '2025-11-25', capabilities: {}, clientInfo: { name: 'behave-raw', version: '1' } });
    c.notify('notifications/initialized');
    row.navigate1 = (await c.call('browser_navigate', { url: `${site.origin}/echo` })).isError;
    row.navigate2 = (await c.call('browser_navigate', { url: `${site.origin}/echo?second=1` })).isError;
    const ev = await c.call('browser_evaluate', { function: READ_UA });
    const literal = ev.text.split(String.fromCharCode(10)).map((s) => s.trim()).find((s) => s.startsWith('"{'));
    try { row.page = JSON.parse(JSON.parse(literal)); } catch { row.page = { unparsed: ev.text.slice(0, 800) }; }
    // A service worker of the page's own, and what its fetch sends.
    const sw = await c.call('browser_evaluate', { function: `async () => {
      const reg = await navigator.serviceWorker.register('/sw.js');
      await navigator.serviceWorker.ready;
      const r = await fetch('/headers?from=sw-check', { cache: 'no-store' });
      return JSON.stringify({ scope: reg.scope, controlled: !!navigator.serviceWorker.controller, answered: (await r.json()).ua });
    }` });
    row.serviceWorker = sw.text.slice(0, 600);
    await c.call('browser_tabs', { action: 'new', url: `${site.origin}/echo?tab=second` });
    await c.call('browser_close', {});
    // The next call starts a new browser in the same child, and the session
    // restore reopens both tabs: their first requests are what this reads.
    const restoreFrom = site.hits.length;
    row.restoreList = (await c.call('browser_tabs', { action: 'list' })).text.slice(0, 600);
    await new Promise((r) => setTimeout(r, 4000));
    row.restoreHits = site.hits.slice(restoreFrom).map((h) => ({ path: h.path, ua: h.ua, chUa: h.chUa }));
    await c.call('browser_close', {});
  } catch (e) {
    row.error = String(e?.message ?? e);
  }
  row.hits = site.hits.slice(hitsBefore);
  c.child.stdin.end();
  await new Promise((res) => { c.child.on('exit', (code) => { row.exitCode = code; res(); }); setTimeout(res, 30000); });
  results.arms.push(row);
  log(arm.name, JSON.stringify(row.page ?? row.error).slice(0, 600));
  save();
}

await site.close();
results.endedUtc = new Date().toISOString();
save();
setTimeout(() => process.exit(0), 300);
