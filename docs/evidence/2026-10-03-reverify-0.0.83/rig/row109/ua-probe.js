// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Re-establishes re-verification row 109 on 2026-10-03: the user agent and
// navigator.webdriver through a hand-written @playwright/mcp config alone, one
// child per arm, headless, read back with browser_evaluate. Written for this
// re-take because the 2026-08-19 harness was not kept; the arms are the six
// rows of the kb table, in its order, and the procedure is the one the kb
// entry states.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { makeClient } = require('./rpc.js');

const W = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\wt\\stale';
const NODE = path.join(W, 'payload', 'node', 'node.exe');
const CLI = path.join(W, 'payload', 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js');
const BROWSERS = path.join(process.env.LOCALAPPDATA, 'BrowserAI', 'browsers');
const OUT = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch\\out\\row109';
const TMP = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch\\tmp';

const CHROME_UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0.0.0 Safari/537.36';
const ARMS = [
  { name: 'chromium-nothing', browserName: 'chromium' },
  { name: 'chromium-contextOptions-userAgent', browserName: 'chromium', contextOptions: { userAgent: CHROME_UA } },
  { name: 'firefox-nothing', browserName: 'firefox' },
  { name: 'firefox-contextOptions-userAgent', browserName: 'firefox', contextOptions: { userAgent: 'BrowserAI-probe/1.0 distinct-context-option' } },
  { name: 'firefox-pref-dom.webdriver.enabled-false', browserName: 'firefox', prefs: { 'dom.webdriver.enabled': false } },
  { name: 'firefox-pref-general.useragent.override-CONTROL', browserName: 'firefox', prefs: { 'general.useragent.override': 'BrowserAI-probe/1.0 distinct-pref' } },
];

fs.rmSync(OUT, { recursive: true, force: true });
fs.mkdirSync(OUT, { recursive: true });
const lines = [];
const log = (...a) => { const s = [new Date().toISOString(), ...a].join(' '); lines.push(s); console.log(s); fs.appendFileSync(path.join(OUT, 'ua-probe.log'), s + '\n'); };

(async () => {
  const server = http.createServer((req, res) => {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end('<!doctype html><meta charset=utf-8><title>ua</title><p>ua</p>');
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const url = 'http://127.0.0.1:' + server.address().port + '/';
  log('origin', url, 'node', NODE, 'cli', CLI, 'browsers', BROWSERS);

  const results = [];
  for (const arm of ARMS) {
    const dir = path.join(OUT, arm.name);
    fs.mkdirSync(path.join(dir, 'output'), { recursive: true });
    const launchOptions = { headless: true };
    if (arm.browserName === 'chromium') launchOptions.channel = 'chrome-for-testing';
    if (arm.prefs) launchOptions.firefoxUserPrefs = arm.prefs;
    const config = {
      browser: {
        browserName: arm.browserName,
        userDataDir: path.join(dir, 'profile'),
        launchOptions,
        ...(arm.contextOptions ? { contextOptions: arm.contextOptions } : {}),
      },
      outputDir: path.join(dir, 'output'),
    };
    const configPath = path.join(dir, 'config.json');
    fs.writeFileSync(configPath, JSON.stringify(config, null, 2));
    const env = {};
    for (const [k, v] of Object.entries(process.env)) { if (!/^(PLAYWRIGHT_MCP|DEBUG|NODE_OPTIONS|NODE_PATH)/i.test(k)) env[k] = v; }
    Object.assign(env, { PLAYWRIGHT_BROWSERS_PATH: BROWSERS, PLAYWRIGHT_SKIP_BROWSER_GC: '1', PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1', TEMP: TMP, TMP });
    const c = makeClient(NODE, [CLI, '--config', configPath], { cwd: dir, env, stderrLog: path.join(dir, 'child.stderr.log') });
    const textOf = r => ((r.msg.result && r.msg.result.content) || []).filter(x => x.type === 'text').map(x => x.text).join('\n');
    const row = { arm: arm.name, config: config.browser };
    try {
      await c.rpc('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'row109', version: '1' } }, 120000);
      c.notify('notifications/initialized');
      const nav = await c.rpc('tools/call', { name: 'browser_navigate', arguments: { url } }, 240000);
      row.navigateError = !!(nav.msg.result && nav.msg.result.isError);
      const ev = await c.rpc('tools/call', { name: 'browser_evaluate', arguments: { function: '() => JSON.stringify({ ua: navigator.userAgent, webdriver: navigator.webdriver })' } }, 120000);
      const t = textOf(ev);
      row.raw = t;
      // The result section holds one JSON string literal whose content is the object.
      const literal = t.split('\n').map(s => s.trim()).find(s => s.startsWith('"{'));
      try { row.value = JSON.parse(JSON.parse(literal)); } catch { row.value = null; }
      await c.rpc('tools/call', { name: 'browser_close', arguments: {} }, 120000);
    } catch (e) {
      row.error = String(e && e.message || e);
    }
    c.child.stdin.end();
    await new Promise(res => { c.child.on('exit', code => { row.exitCode = code; res(); }); setTimeout(res, 20000); });
    results.push(row);
    log(arm.name, '->', row.value ? JSON.stringify(row.value) : ('UNPARSED ' + JSON.stringify((row.raw || row.error || '').slice(0, 600))));
  }
  fs.writeFileSync(path.join(OUT, 'results.json'), JSON.stringify(results, null, 2));
  server.close();
  log('done');
  process.exit(0);
})().catch(e => { log('FAILED', (e && e.stack) || String(e)); process.exit(1); });
