// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// What a page and a server can see about the browser, per launch arm.
//
// The page computes the fingerprint itself and POSTs it back, so an arm can be
// measured with NO automation client attached at all -- which is the only way to
// tell "Playwright's connection enables AutomationControlled" from "the build
// does" or "the switch does".
//
//   node fp.mjs direct   <arm> [extra switches...]      chrome.exe, launched by us, nothing driving it
//   node fp.mjs pw       <arm> [--headless] [--nab] [--enable-automation]
//   node fp.mjs funnel   <arm> [--headless] [--enable-automation]
//
// `--nab` is --disable-blink-features=AutomationControlled.

import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { createServer } from 'node:http';
import path from 'node:path';

const HERE = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const REPO = path.join('C:', 'Source', 'SixFive7', 'BrowserAI');
const PW = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const nodeExe = path.join(REPO, 'payload', 'node', 'node.exe');
const cli = path.join(REPO, 'payload', 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js');
const BROWSERS = path.join(process.env.LOCALAPPDATA, 'BrowserAI', 'browsers');
const CHROME = path.join(BROWSERS, 'chromium-1246', 'chrome-win64', 'chrome.exe');

const mode = process.argv[2];
const arm = process.argv[3];
const rest = process.argv.slice(4);
const has = (f) => rest.includes(f);

const root = path.join(HERE, 'fp', `${mode}-${arm}`);
rmSync(root, { recursive: true, force: true });
mkdirSync(root, { recursive: true });
const profile = path.join(root, 'profile');

const log = [];
const say = (...a) => { const l = a.join(' '); log.push(l); console.log(l); };

// The page. Everything a bot-detection script routinely reads, computed in the
// page and posted back, so no evaluation channel is needed.
const PAGE = `<!doctype html><meta charset=utf-8><title>fp</title><body><h1>fingerprint</h1><script>
(async () => {
  const g = (f) => { try { return f(); } catch (e) { return 'THREW: ' + e.message; } };
  let gl = null, glv = null, glr = null;
  try {
    const c = document.createElement('canvas').getContext('webgl');
    const d = c.getExtension('WEBGL_debug_renderer_info');
    glv = c.getParameter(d.UNMASKED_VENDOR_WEBGL); glr = c.getParameter(d.UNMASKED_RENDERER_WEBGL);
  } catch (e) { gl = 'THREW: ' + e.message; }
  let permMismatch = null;
  try {
    const st = (await navigator.permissions.query({ name: 'notifications' })).state;
    permMismatch = JSON.stringify({ permissionsState: st, notificationPermission: Notification.permission, mismatch: Notification.permission === 'denied' && st === 'prompt' });
  } catch (e) { permMismatch = 'THREW: ' + e.message; }
  let uaData = null;
  try {
    uaData = JSON.stringify(await navigator.userAgentData.getHighEntropyValues(['architecture','bitness','model','platformVersion','uaFullVersion','fullVersionList','wow64']));
  } catch (e) { uaData = 'THREW: ' + e.message; }
  const report = {
    webdriver: g(() => navigator.webdriver),
    webdriverOwnProperty: g(() => Object.prototype.hasOwnProperty.call(navigator, 'webdriver')),
    webdriverDescriptorOnProto: g(() => { const d = Object.getOwnPropertyDescriptor(Navigator.prototype, 'webdriver'); return d ? String(d.get) : 'none'; }),
    documentWebdriverAttr: g(() => document.documentElement.getAttribute('webdriver')),
    userAgent: g(() => navigator.userAgent),
    appVersion: g(() => navigator.appVersion),
    languages: g(() => JSON.stringify(navigator.languages)),
    language: g(() => navigator.language),
    platform: g(() => navigator.platform),
    hardwareConcurrency: g(() => navigator.hardwareConcurrency),
    deviceMemory: g(() => navigator.deviceMemory),
    pdfViewerEnabled: g(() => navigator.pdfViewerEnabled),
    pluginsLength: g(() => navigator.plugins.length),
    mimeTypesLength: g(() => navigator.mimeTypes.length),
    uaDataBrands: g(() => JSON.stringify(navigator.userAgentData && navigator.userAgentData.brands)),
    uaDataMobile: g(() => navigator.userAgentData && navigator.userAgentData.mobile),
    uaDataPlatform: g(() => navigator.userAgentData && navigator.userAgentData.platform),
    uaDataHighEntropy: uaData,
    windowChrome: g(() => typeof window.chrome),
    windowChromeKeys: g(() => window.chrome ? Object.keys(window.chrome).sort().join(',') : 'none'),
    chromeRuntime: g(() => window.chrome && typeof window.chrome.runtime),
    chromeRuntimeKeys: g(() => (window.chrome && window.chrome.runtime) ? Object.keys(window.chrome.runtime).sort().join(',') : 'none'),
    chromeLoadTimes: g(() => window.chrome && typeof window.chrome.loadTimes),
    chromeCsi: g(() => window.chrome && typeof window.chrome.csi),
    chromeApp: g(() => (window.chrome && window.chrome.app) ? Object.keys(window.chrome.app).sort().join(',') : 'none'),
    cdcKeys: g(() => Object.getOwnPropertyNames(window).filter((k) => /^(cdc_|\\$cdc|__webdriver|__selenium|__fxdriver|_Selenium|__driver|__playwright|__pw|_phantom|callPhantom|domAutomation)/i.test(k)).join(',') || 'none'),
    documentCdc: g(() => Object.getOwnPropertyNames(document).filter((k) => /^(\\$cdc|cdc_)/i.test(k)).join(',') || 'none'),
    externalToString: g(() => String(window.external)),
    outerWidth: g(() => window.outerWidth),
    outerHeight: g(() => window.outerHeight),
    innerWidth: g(() => window.innerWidth),
    innerHeight: g(() => window.innerHeight),
    screen: g(() => screen.width + 'x' + screen.height + '@' + screen.colorDepth),
    devicePixelRatio: g(() => window.devicePixelRatio),
    connection: g(() => navigator.connection ? navigator.connection.effectiveType + '/' + navigator.connection.rtt : 'none'),
    maxTouchPoints: g(() => navigator.maxTouchPoints),
    notificationPermission: permMismatch,
    webglVendor: glv, webglRenderer: glr, webglError: gl,
    timezone: g(() => Intl.DateTimeFormat().resolvedOptions().timeZone),
    permissionsQueryToString: g(() => String(navigator.permissions.query)),
    errorStackHasPuppeteer: g(() => { try { null.f(); } catch (e) { return /puppeteer|playwright|devtools/i.test(e.stack || '') ; } return false; }),
  };
  await fetch('/report', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(report) });
  document.title = 'reported';
})();
</script></body>`;

let reported = null;
const headers = {};
const server = createServer((req, res) => {
  headers[req.method + ' ' + req.url] = req.headers;
  if (req.method === 'POST' && req.url === '/report') {
    let body = '';
    req.on('data', (c) => { body += c; });
    req.on('end', () => { reported = JSON.parse(body); res.writeHead(204); res.end(); });
    return;
  }
  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
  res.end(PAGE);
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const origin = `http://127.0.0.1:${server.address().port}/`;
say(`[fp] mode=${mode} arm=${arm} rest=${JSON.stringify(rest)} origin=${origin}`);

const waitForReport = async (limitMs) => {
  const started = Date.now();
  while (reported === null && Date.now() - started < limitMs) {
    await new Promise((r) => setTimeout(r, 250));
  }
  return reported;
};

let cleanup = async () => {};

if (mode === 'direct') {
  // The provisioned binary, launched by us, with NOTHING driving it: no
  // remote-debugging pipe, no port, no client. Killed by pid at the end.
  const args = [`--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check', ...rest.filter((a) => a.startsWith('--')), origin];
  say(`[fp] ${CHROME} ${args.join(' ')}`);
  const child = spawn(CHROME, args, { detached: false, stdio: 'ignore' });
  say(`[fp] chrome pid ${child.pid}`);
  cleanup = async () => {
    // Only what we launched: the pid we were handed, and its tree.
    try { execFileSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' }); } catch { /* already gone */ }
  };
} else if (mode === 'pw') {
  process.env.PLAYWRIGHT_BROWSERS_PATH = BROWSERS;
  const pw = await import(new URL('file:///' + path.join(PW, 'index.mjs').replace(/\\/g, '/')).href);
  const { chromium } = pw.default ?? pw;
  const options = {
    headless: has('--headless'),
    channel: 'chrome-for-testing',
    viewport: { width: 1280, height: 800 },
    locale: 'en-US',
    args: [
      ...(has('--nab') ? ['--disable-blink-features=AutomationControlled'] : []),
      ...(has('--enable-automation') ? ['--enable-automation'] : []),
    ],
  };
  say(`[fp] playwright launchOptions=${JSON.stringify(options)}`);
  const context = await chromium.launchPersistentContext(profile, options);
  const page = context.pages()[0] ?? await context.newPage();
  await page.goto(origin, { waitUntil: 'load' });
  cleanup = async () => { await context.close(); };
} else if (mode === 'funnel') {
  const outputDir = path.join(root, 'output');
  mkdirSync(outputDir, { recursive: true });
  const config = {
    browser: {
      browserName: 'chromium',
      userDataDir: profile,
      launchOptions: {
        headless: has('--headless'),
        downloadsPath: path.join(root, 'downloads'),
        channel: 'chrome-for-testing',
        ...(has('--enable-automation')
          ? { args: ['--enable-automation', '--disable-blink-features=AutomationControlled'] }
          : {}),
      },
      contextOptions: { viewport: { width: 1280, height: 800 }, locale: 'en-US', ignoreHTTPSErrors: true, permissions: ['clipboard-read'] },
    },
    capabilities: ['config', 'vision', 'devtools', 'storage', 'network', 'pdf', 'testing'],
    outputDir, saveSession: false, allowUnrestrictedFileAccess: false,
    console: { level: 'debug' }, snapshot: { boxes: true }, codegen: 'none',
    filePaths: 'absolute', timeouts: { idle: 3600000 }, webmcp: true,
  };
  const configPath = path.join(root, 'config.json');
  writeFileSync(configPath, JSON.stringify(config, null, 2), 'utf8');
  say(`[fp] funnel launchOptions=${JSON.stringify(config.browser.launchOptions)}`);
  const env = {};
  for (const [k, v] of Object.entries(process.env)) { if (!/^(PLAYWRIGHT_MCP|DEBUG|NODE_OPTIONS|NODE_PATH)/i.test(k)) env[k] = v; }
  env.PLAYWRIGHT_BROWSERS_PATH = BROWSERS;
  const child = spawn(nodeExe, [cli, '--config', configPath, '--sandbox'], { cwd: root, env, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  let pending = ''; const waiting = new Map(); let nextId = 1; let stderr = '';
  child.stdout.setEncoding('utf8'); child.stderr.setEncoding('utf8');
  child.stderr.on('data', (c) => { stderr += c; });
  child.stdout.on('data', (c) => {
    pending += c;
    for (let e = pending.indexOf('\n'); e >= 0; e = pending.indexOf('\n')) {
      const line = pending.slice(0, e).trim(); pending = pending.slice(e + 1);
      if (!line) continue; let m; try { m = JSON.parse(line); } catch { continue; }
      if (m.id !== undefined && waiting.has(m.id)) { waiting.get(m.id)(m); waiting.delete(m.id); }
    }
  });
  const request = (method, params) => { const id = nextId++; return new Promise((ok, fail) => { const t = setTimeout(() => fail(new Error(`${method} timed out. ${stderr}`)), 180000); waiting.set(id, (m) => { clearTimeout(t); ok(m); }); child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`); }); };
  await request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'fp', version: '1' } });
  child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' })}\n`);
  await request('tools/call', { name: 'browser_navigate', arguments: { url: origin } });
  cleanup = async () => { try { await request('tools/call', { name: 'browser_close', arguments: {} }); } catch { /* ignore */ } child.stdin.end(); };
} else {
  throw new Error(`unknown mode ${mode}`);
}

const result = await waitForReport(120000);
say(`[fp] reported=${result !== null}`);
writeFileSync(path.join(root, 'fingerprint.json'), JSON.stringify({ mode, arm, rest, report: result, headers }, null, 2), 'utf8');
if (result) {
  for (const k of Object.keys(result)) say(`[fp] ${k.padEnd(26)} ${JSON.stringify(result[k])}`);
}
const pageHeaders = headers['GET /'] ?? {};
say(`[fp] request headers: ${JSON.stringify(pageHeaders)}`);

await cleanup();
server.close();
writeFileSync(path.join(root, 'fp.log'), log.join('\n'), 'utf8');
setTimeout(() => process.exit(0), 1500);
