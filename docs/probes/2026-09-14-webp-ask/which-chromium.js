// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

'use strict';
// Which browser binary does the raw child actually drive under --browser chromium,
// and what full version is it? The reduced UA says 153.0.0.0 and nothing more, so
// this reads the high-entropy version AND walks the live process tree by pid.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { execFileSync } = require('node:child_process');
const { makeClient, BROWSER_HANG_MS } = require('./rpc.js');

const REPO = 'c:\\Source\\SixFive7\\BrowserAI';
const OUT = path.join(REPO, '.work', '2026-09-14-webp-ask', 'which');
const NODE = path.join(REPO, 'payload', 'node', 'node.exe');
const CLI = path.join(REPO, 'payload', 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js');
const BROWSERS = 'C:\\Users\\jori\\AppData\\Local\\BrowserAI\\browsers';
fs.rmSync(OUT, { recursive: true, force: true });
fs.mkdirSync(path.join(OUT, 'output'), { recursive: true });
const log = (...a) => { const s = [new Date().toISOString(), ...a].join(' '); console.log(s); fs.appendFileSync(path.join(OUT, 'which.log'), s + '\n'); };

function treeOf(rootPid) {
  const ps = 'Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath | ConvertTo-Json -Depth 3 -Compress';
  const raw = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const all = JSON.parse(raw);
  const byParent = new Map();
  for (const p of all) { const k = p.ParentProcessId; if (!byParent.has(k)) byParent.set(k, []); byParent.get(k).push(p); }
  const out = []; const stack = [rootPid];
  while (stack.length) {
    const pid = stack.pop();
    for (const ch of (byParent.get(pid) || [])) { out.push({ pid: ch.ProcessId, ppid: ch.ParentProcessId, name: ch.Name, exe: ch.ExecutablePath }); stack.push(ch.ProcessId); }
  }
  return out;
}

(async () => {
  const server = http.createServer((req, res) => {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end('<!doctype html><html><head><meta charset="utf-8"><title>v</title></head><body><p>v</p></body></html>');
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const base = 'http://127.0.0.1:' + server.address().port;

  const env = Object.assign({}, process.env, {
    PLAYWRIGHT_BROWSERS_PATH: BROWSERS,
    PLAYWRIGHT_SKIP_BROWSER_GC: '1',
    PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1',
  });
  const facts = {};
  for (const tag of ['chromium', 'default']) {
    const outDir = path.join(OUT, 'output', tag);
    fs.mkdirSync(outDir, { recursive: true });
    const args = [CLI, '--headless', '--isolated', '--output-dir', outDir, '--viewport-size', '1280x720'];
    if (tag === 'chromium') args.push('--browser', 'chromium');
    const c = makeClient(NODE, args, { cwd: OUT, env, stderrLog: path.join(OUT, tag + '.stderr.log') });
    log(tag, 'child pid', c.child.pid);
    await c.rpc('initialize', { protocolVersion: '2025-11-25', capabilities: {}, clientInfo: { name: 'which', version: '1' } }, 120000);
    c.notify('notifications/initialized');
    const call = (n, a) => c.rpc('tools/call', { name: n, arguments: a || {} }, BROWSER_HANG_MS);
    const textOf = r => ((r.msg.result && r.msg.result.content) || []).filter(x => x.type === 'text').map(x => x.text).join('\n');
    await call('browser_navigate', { url: base + '/' });
    const hv = await call('browser_evaluate', { function: "async () => JSON.stringify(await navigator.userAgentData.getHighEntropyValues(['uaFullVersion','fullVersionList']))" });
    const tree = treeOf(c.child.pid);
    facts[tag] = {
      childPid: c.child.pid,
      highEntropy: textOf(hv).slice(0, 900),
      browserExes: [...new Set(tree.filter(p => /chrome|msedge|firefox/i.test(p.name || '')).map(p => p.exe))],
      treeSize: tree.length,
    };
    log(tag, 'browser exes:', JSON.stringify(facts[tag].browserExes));
    log(tag, 'high entropy:', facts[tag].highEntropy.replace(/\s+/g, ' ').slice(0, 300));
    await call('browser_close', {});
    c.child.stdin.end();
    await new Promise(res => { c.child.on('exit', code => { log(tag, 'exited', code); res(); }); setTimeout(res, 20000); });
    const after = treeOf(c.child.pid);
    facts[tag].survivorsAfterExit = after.map(p => ({ pid: p.pid, name: p.name, exe: p.exe }));
    log(tag, 'survivors under that pid after exit:', after.length);
  }
  fs.writeFileSync(path.join(OUT, 'which.json'), JSON.stringify(facts, null, 2));
  server.close();
  log('=== done ===');
  process.exit(0);
})().catch(e => { log('FAILED:', (e && e.stack) || String(e)); process.exit(1); });
