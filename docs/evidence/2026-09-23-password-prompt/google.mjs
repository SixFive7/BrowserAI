// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// One real Google search per run, from a fresh scratch profile, classified and
// screenshotted. Headed, because that is the shape the maintainer asked about.
//
//   node google.mjs <funnel|pw> <arm> "<query>" [--enable-automation] [--nab]
//
// `funnel` is the product's own path (payload node + @playwright/mcp cli.js +
// a config shaped like BrowserConfiguration.Generate's). `pw` is raw
// playwright-core with Playwright's own defaults and nothing else.

import { spawn, execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync, rmSync, readFileSync, existsSync } from 'node:fs';
import path from 'node:path';

const HERE = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const REPO = path.join('C:', 'Source', 'SixFive7', 'BrowserAI');
const PW = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const nodeExe = path.join(REPO, 'payload', 'node', 'node.exe');
const cli = path.join(REPO, 'payload', 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js');
const BROWSERS = path.join(process.env.LOCALAPPDATA, 'BrowserAI', 'browsers');

const mode = process.argv[2];
const arm = process.argv[3];
const query = process.argv[4];
const rest = process.argv.slice(5);
const has = (f) => rest.includes(f);

const root = path.join(HERE, 'google', `${mode}-${arm}`);
rmSync(root, { recursive: true, force: true });
mkdirSync(root, { recursive: true });
const profile = path.join(root, 'profile');
const outputDir = path.join(root, 'output');
mkdirSync(outputDir, { recursive: true });

const log = [];
const say = (...a) => { const l = a.join(' '); log.push(l); console.log(l); };

const realistic = rest.includes('--realistic');
const url = realistic ? 'https://www.google.com/?hl=en' : `https://www.google.com/search?q=${encodeURIComponent(query)}&hl=en`;
say(`[g] mode=${mode} arm=${arm} rest=${JSON.stringify(rest)}`);
say(`[g] ${url}`);

// The classifier, as a source string so both drivers can use the same one.
const CLASSIFIER = `() => {
  const t = document.body ? document.body.innerText.slice(0, 4000) : '';
  const has = (s) => t.toLowerCase().includes(s);
  return JSON.stringify({
    url: location.href,
    title: document.title,
    rso: !!document.querySelector('#rso'),
    search: !!document.querySelector('#search'),
    resultLinks: document.querySelectorAll('#search a h3, #rso a h3').length,
    recaptchaIframe: document.querySelectorAll('iframe[src*="recaptcha"], iframe[title*="recaptcha" i]').length,
    gRecaptchaDiv: document.querySelectorAll('div.g-recaptcha, #recaptcha').length,
    unusualTraffic: has('unusual traffic') || has('ongebruikelijk verkeer'),
    sorryPath: location.pathname.indexOf('/sorry') === 0,
    consentHost: location.host.indexOf('consent.') === 0,
    consentText: has('before you continue') || has('voordat je verdergaat') || has('accept all'),
    notRobot: has("i'm not a robot") || has('ik ben geen robot'),
    bodyHead: t.slice(0, 500),
  });
}`;

let verdict = null;
let cleanup = async () => {};

if (mode === 'funnel') {
  const config = {
    browser: {
      browserName: 'chromium',
      userDataDir: profile,
      launchOptions: {
        headless: false,
        downloadsPath: path.join(root, 'downloads'),
        channel: 'chrome-for-testing',
        ...(has('--enable-automation') ? { args: ['--enable-automation', '--disable-blink-features=AutomationControlled'] } : {}),
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
  say(`[g] funnel launchOptions=${JSON.stringify(config.browser.launchOptions)}`);
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
  const call = (n, a) => request('tools/call', { name: n, arguments: a ?? {} });
  const textOf = (m) => (m.result?.content ?? []).filter((b) => b.type === 'text').map((b) => b.text).join('\n');
  await request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'google-probe', version: '1' } });
  child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' })}\n`);
  const nav = await call('browser_navigate', { url });
  writeFileSync(path.join(root, 'navigate.txt'), textOf(nav), 'utf8');
  await new Promise((r) => setTimeout(r, 4000));
  if (realistic) {
    // A person's route: the home page, dismiss the EU consent dialog, then the
    // box, then Enter. Without the dismissal the dialog swallows the submit --
    // measured, and it is what made the first realistic run classify the HOME
    // page instead of a result page.
    const snapshot = async (name) => {
      const s = textOf(await call('browser_snapshot', {}));
      writeFileSync(path.join(root, name), s, 'utf8');
      return s;
    };
    const refFor = (text, pattern) => {
      const line = text.split('\n').find((l) => pattern.test(l) && l.includes('[ref='));
      return line ? /\[ref=([^\]]+)\]/.exec(line)?.[1] : null;
    };
    let snapText = await snapshot('home-snapshot.txt');
    const reject = refFor(snapText, /button "Reject all"/);
    say(`[g] realistic: consent reject ref=${reject}`);
    if (reject) {
      await call('browser_click', { element: 'Reject all', target: reject });
      await new Promise((r) => setTimeout(r, 3000));
      snapText = await snapshot('home-after-consent.txt');
    }
    const ref = refFor(snapText, /combobox|searchbox|textbox/i);
    say(`[g] realistic: search box ref=${ref}`);
    if (ref) {
      await call('browser_type', { element: 'search box', target: ref, text: query, submit: true });
      await new Promise((r) => setTimeout(r, 6000));
    }
  }
  const ev = await call('browser_evaluate', { function: CLASSIFIER });
  writeFileSync(path.join(root, 'evaluate.txt'), textOf(ev), 'utf8');
  const shot = await call('browser_take_screenshot', { scale: 'css', filename: path.join(outputDir, 'page.png') });
  writeFileSync(path.join(root, 'screenshot.txt'), textOf(shot), 'utf8');
  const m = /\{[\s\S]*\}/.exec(textOf(ev).replace(/\\"/g, '"'));
  try { verdict = JSON.parse(/### Result[\s\S]*?"(\{[\s\S]*?\})"/.exec(textOf(ev))?.[1]?.replace(/\\"/g, '"') ?? m[0]); } catch { verdict = { raw: textOf(ev).slice(0, 2000) }; }
  cleanup = async () => { try { await call('browser_close', {}); } catch { /* ignore */ } child.stdin.end(); };
} else {
  process.env.PLAYWRIGHT_BROWSERS_PATH = BROWSERS;
  const pw = await import(new URL('file:///' + path.join(PW, 'index.mjs').replace(/\\/g, '/')).href);
  const { chromium } = pw.default ?? pw;
  const options = {
    headless: false,
    channel: 'chrome-for-testing',
    viewport: { width: 1280, height: 800 },
    locale: 'en-US',
    args: [...(has('--nab') ? ['--disable-blink-features=AutomationControlled'] : []), ...(has('--enable-automation') ? ['--enable-automation'] : [])],
  };
  say(`[g] pw launchOptions=${JSON.stringify(options)}`);
  const context = await chromium.launchPersistentContext(profile, options);
  const page = context.pages()[0] ?? await context.newPage();
  await page.goto(url, { waitUntil: 'load', timeout: 120000 });
  await page.waitForTimeout(4000);
  if (realistic) {
    const box = page.locator('textarea[name=q], input[name=q]').first();
    if (await box.count()) { await box.click(); await box.type(query, { delay: 60 }); await page.keyboard.press('Enter'); await page.waitForTimeout(5000); }
  }
  verdict = JSON.parse(await page.evaluate(`(${CLASSIFIER})()`));
  await page.screenshot({ path: path.join(outputDir, 'page.png') });
  cleanup = async () => { await context.close(); };
}

say(`[g] VERDICT ${JSON.stringify(verdict, null, 1)}`);
writeFileSync(path.join(root, 'verdict.json'), JSON.stringify({ mode, arm, query, url, rest, verdict }, null, 2), 'utf8');
await cleanup();
writeFileSync(path.join(root, 'google.log'), log.join('\n'), 'utf8');
setTimeout(() => process.exit(0), 1500);
