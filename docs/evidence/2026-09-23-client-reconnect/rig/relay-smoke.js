// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

const { spawn } = require('child_process');
const fs = require('fs');
const F = 'C:/Source/SixFive7/BrowserAI/.work/q254-2026-09-23';
const TAG = process.argv[2] || 'rsmoke';
const LOGDIR = F + '/logs';
const SWAP_REQ = LOGDIR + '/' + TAG + '.swap.request';
const APPLY_DONE = LOGDIR + '/' + TAG + '.apply.done';
for (const p of [SWAP_REQ, APPLY_DONE, LOGDIR + '/' + TAG + '.handover.log']) { try { fs.unlinkSync(p); } catch (e) {} }
fs.writeFileSync(F + '/ho-install/VERSION', '1.0.0\n');
const env = { ...process.env, HO_LOGDIR: LOGDIR, HO_TAG: TAG, HO_INSTALL_DIR: F + '/ho-install',
  HO_SWAP_REQUEST: SWAP_REQ, HO_APPLY_DONE: APPLY_DONE, HO_ANNOUNCE_LIST_CHANGED: process.env.HO_ANNOUNCE_LIST_CHANGED || '0' };
const t0 = Date.now();
const c = spawn(process.execPath, [F + '/ho-helper/relay.js'], { stdio: ['pipe', 'pipe', 'pipe'], env });
c.on('exit', (code) => console.log('EVENT relay exit code=' + code + ' at +' + (Date.now() - t0) + 'ms'));
c.on('close', () => console.log('EVENT relay close at +' + (Date.now() - t0) + 'ms'));
c.stdout.on('end', () => console.log('EVENT relay stdout EOF at +' + (Date.now() - t0) + 'ms'));
c.stderr.on('data', (d) => console.log('STDERR: ' + d.toString().trim().slice(0, 200)));
let buf = ''; const waiters = new Map(); let id = 0;
c.stdout.on('data', (d) => {
  buf += d.toString('utf8'); let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim(); buf = buf.slice(nl + 1);
    if (!line) continue;
    const m = JSON.parse(line);
    if (m.id !== undefined) { console.log('RECV id=' + m.id + ' ' + JSON.stringify(m.result ?? m.error)); const w = waiters.get(m.id); if (w) { waiters.delete(m.id); w(m); } }
    else console.log('RECV notification ' + m.method);
  }
});
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
function send(method, params) {
  const myId = ++id;
  c.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: myId, method, params }) + '\n');
  return new Promise((res) => { const t = setTimeout(() => { console.log('TIMEOUT id=' + myId + ' ' + method); res({ timeout: true }); }, 30000); waiters.set(myId, (m) => { clearTimeout(t); res(m); }); });
}
(async () => {
  await send('initialize', { protocolVersion: '2025-06-18', capabilities: { roots: { listChanged: true } }, clientInfo: { name: 'smoke', version: '1' } });
  c.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  await send('tools/list');
  console.log('--- call 1 (v1.0.0) ---');
  await send('tools/call', { name: 'ping', arguments: { note: 'c1' } });
  console.log('--- request a swap, then run the apply ---');
  fs.writeFileSync(SWAP_REQ, 'now\n');
  await send('tools/call', { name: 'ping', arguments: { note: 'c2-during-swap' } });
  await sleep(300);
  fs.writeFileSync(F + '/ho-install/VERSION', '2.0.0\n');
  fs.writeFileSync(APPLY_DONE, 'done\n');
  await sleep(2500);
  console.log('--- call 3 (must be v2.0.0, no user action) ---');
  await send('tools/call', { name: 'ping', arguments: { note: 'c3' } });
  await sleep(400);
  c.stdin.end();
  await sleep(1200);
  process.exit(0);
})();
