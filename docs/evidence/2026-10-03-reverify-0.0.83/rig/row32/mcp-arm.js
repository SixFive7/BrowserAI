// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Row 32, arm (a), 2026-10-03: an @playwright/mcp child, headless on Playwright's
// own argument list, given a userDataDir that is occupied by a FILE. Records
// whether initialize and browser_navigate succeed, then at 25 s after launch reads
// the tree (processes, message-window titles, restart registration) through
// measure.ps1, then closes the browser and ends the child.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const { makeClient } = require('../row122/rpc.js');

const W = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\wt\\stale';
const S = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch';
const OUT = path.join(S, 'out', 'row32', 'a-mcp');
const NODE = path.join(W, 'payload', 'node', 'node.exe');
const CLI = path.join(W, 'payload', 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js');
fs.rmSync(OUT, { recursive: true, force: true });
fs.mkdirSync(path.join(OUT, 'output'), { recursive: true });
const occupied = path.join(OUT, 'occupied-by-a-file');
fs.writeFileSync(occupied, 'a file where the profile directory should be\n');
const log = (...a) => { const s = [new Date().toISOString(), ...a].join(' '); console.log(s); fs.appendFileSync(path.join(OUT, 'mcp-arm.log'), s + '\n'); };
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const config = { browser: { browserName: 'chromium', userDataDir: occupied, launchOptions: { headless: true, channel: 'chrome-for-testing' } }, outputDir: path.join(OUT, 'output') };
  fs.writeFileSync(path.join(OUT, 'config.json'), JSON.stringify(config, null, 2));
  const env = {};
  for (const [k, v] of Object.entries(process.env)) { if (!/^(PLAYWRIGHT_MCP|DEBUG|NODE_OPTIONS|NODE_PATH)/i.test(k)) env[k] = v; }
  Object.assign(env, { PLAYWRIGHT_BROWSERS_PATH: path.join(process.env.LOCALAPPDATA, 'BrowserAI', 'browsers'), PLAYWRIGHT_SKIP_BROWSER_GC: '1', PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1', TEMP: path.join(S, 'tmp'), TMP: path.join(S, 'tmp') });
  const t0 = Date.now();
  const c = makeClient(NODE, [CLI, '--config', path.join(OUT, 'config.json')], { cwd: OUT, env, stderrLog: path.join(OUT, 'child.stderr.log') });
  log('child pid', c.child.pid, 'userDataDir (a file)', occupied);
  const textOf = r => ((r.msg.result && r.msg.result.content) || []).filter(x => x.type === 'text').map(x => x.text).join('\n');
  const init = await c.rpc('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'row32', version: '1' } }, 60000);
  log('initialize error?', !!init.msg.error, 'ms', init.ms);
  c.notify('notifications/initialized');
  const nav = await c.rpc('tools/call', { name: 'browser_navigate', arguments: { url: 'data:text/html,<h1>ok</h1>' } }, 240000);
  log('browser_navigate isError', !!(nav.msg.result && nav.msg.result.isError), 'ms', nav.ms, 'text', JSON.stringify(textOf(nav).slice(0, 300)));
  const wait = 25000 - (Date.now() - t0);
  if (wait > 0) await sleep(wait);
  const reading = execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-File', path.join(S, 'rigs', 'row32', 'measure.ps1'), '-Root', String(c.child.pid)], { encoding: 'utf8' }).trim();
  log('at', Date.now() - t0, 'ms after launch:', reading);
  const close = await c.rpc('tools/call', { name: 'browser_close', arguments: {} }, 120000);
  log('browser_close isError', !!(close.msg.result && close.msg.result.isError));
  c.child.stdin.end();
  await new Promise(res => { c.child.on('exit', code => { log('child exited', code); res(); }); setTimeout(res, 20000); });
  process.exit(0);
})().catch(e => { log('FAILED', (e && e.stack) || String(e)); process.exit(1); });
