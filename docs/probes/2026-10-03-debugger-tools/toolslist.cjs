// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch: asks an @playwright/mcp child for tools/list with BrowserAI's
// capability grant and writes the whole result. tools/list is answered from the
// factory's schemas, so no browser is launched.
// Usage: node toolslist.cjs <mcpPackageDir> <out.json>
'use strict';
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const [MCP_DIR, OUT] = process.argv.slice(2);
const SCRATCH = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\debugger-tools';
const dir = path.join(path.dirname(OUT), path.basename(OUT, '.json') + '-cwd');
fs.mkdirSync(dir, { recursive: true });
const cfg = {
  browser: { browserName: 'chromium', userDataDir: path.join(dir, 'profile'), launchOptions: { channel: 'chrome-for-testing', headless: true } },
  capabilities: ['config', 'vision', 'devtools', 'storage', 'network', 'pdf', 'testing'],
  outputDir: path.join(dir, 'output'),
  codegen: 'none',
  webmcp: true,
};
fs.writeFileSync(path.join(dir, 'config.json'), JSON.stringify(cfg, null, 2));
const env = {};
for (const k of ['SystemRoot', 'windir', 'SystemDrive', 'COMSPEC', 'PATH', 'PATHEXT', 'USERPROFILE', 'USERNAME', 'COMPUTERNAME']) if (process.env[k] !== undefined) env[k] = process.env[k];
Object.assign(env, { TEMP: path.join(SCRATCH, 'tmp'), TMP: path.join(SCRATCH, 'tmp'), LOCALAPPDATA: path.join(SCRATCH, 'localappdata'), APPDATA: path.join(SCRATCH, 'appdata'), PWTEST_SERVER_REGISTRY: path.join(SCRATCH, 'registry'), PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1', PLAYWRIGHT_SKIP_BROWSER_GC: '1' });
const proc = spawn(process.execPath, [path.join(MCP_DIR, 'cli.js'), '--config', path.join(dir, 'config.json'), '--sandbox'], { cwd: dir, env, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
let buf = '';
const pending = new Map();
proc.stdout.on('data', (d) => { buf += d; let i; while ((i = buf.indexOf('\n')) >= 0) { const line = buf.slice(0, i); buf = buf.slice(i + 1); try { const m = JSON.parse(line); if (pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } } catch {} } });
let stderr = '';
proc.stderr.on('data', (d) => { stderr += d; });
let id = 1;
const send = (method, params) => new Promise((r) => { const n = id++; pending.set(n, r); proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: n, method, params }) + '\n'); });
const timer = setTimeout(() => { console.error('timeout'); proc.kill(); process.exit(3); }, 60000);
(async () => {
  const init = await send('initialize', { protocolVersion: '2025-11-25', capabilities: {}, clientInfo: { name: 'dbg-toolslist', version: '0' } });
  proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const list = await send('tools/list', {});
  fs.writeFileSync(OUT, JSON.stringify({ serverInfo: init.result && init.result.serverInfo, version: require(path.join(MCP_DIR, 'package.json')).version, tools: list.result.tools }, null, 2));
  proc.stdin.end();
  await new Promise((r) => { proc.on('exit', r); setTimeout(r, 10000); });
  clearTimeout(timer);
  fs.writeFileSync(OUT.replace(/\.json$/, '.stderr.txt'), stderr);
  process.exit(0);
})().catch((e) => { console.error(e); process.exit(1); });
