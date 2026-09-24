// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr


// Raw JSON-RPC driver: validates the handover mechanics with no MCP client involved.
const { spawn } = require('child_process');
const fs = require('fs');
const F = 'C:/Source/SixFive7/BrowserAI/.work/q254-2026-09-23';
const TAG = process.argv[2] || 'smoke';
const LOGDIR = `${F}/logs`;
const STATE = `${F}/logs/${TAG}.state.json`;
const APPLY_DONE = `${F}/logs/${TAG}.apply.done`;
for (const p of [STATE, APPLY_DONE]) { try { fs.unlinkSync(p); } catch (e) {} }
try { fs.unlinkSync(`${LOGDIR}/${TAG}.handover.log`); } catch (e) {}
fs.writeFileSync(`${F}/ho-install/VERSION`, '1.0.0\n');

const env = { ...process.env,
  HO_LOGDIR: LOGDIR, HO_TAG: TAG, HO_TRIGGER: 'after-first-call',
  HO_STATE: STATE, HO_APPLY_DONE: APPLY_DONE,
  HO_HELPER_DIR: `${F}/ho-helper`, HO_INSTALL_DIR: `${F}/ho-install`,
  HO_ANNOUNCE_LIST_CHANGED: process.env.HO_ANNOUNCE_LIST_CHANGED || '0',
};
const child = spawn(process.execPath, [`${F}/ho-install/a.js`], { stdio: ['pipe', 'pipe', 'pipe'], env });
let sawClose = false, sawExit = false, sawStdoutEnd = false;
child.on('close', () => { sawClose = true; console.log('DRIVER: child "close" event (process ended AND streams closed)'); });
child.on('exit', (c) => { sawExit = true; console.log(`DRIVER: child "exit" event code=${c}`); });
child.stdout.on('end', () => { sawStdoutEnd = true; console.log('DRIVER: child stdout EOF'); });
child.stderr.on('data', (d) => console.log('STDERR:', d.toString().trim().slice(0, 200)));

let buf = '';
const waiters = new Map();
child.stdout.on('data', (d) => {
  buf += d.toString('utf8');
  let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim(); buf = buf.slice(nl + 1);
    if (!line) continue;
    const m = JSON.parse(line);
    if (m.id !== undefined) { console.log(`RECV id=${m.id} ${JSON.stringify(m.result ?? m.error)}`); const w = waiters.get(m.id); if (w) { waiters.delete(m.id); w(m); } }
    else console.log(`RECV notification ${m.method}`);
  }
});
let id = 0;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
function send(method, params) {
  const myId = ++id;
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: myId, method, params }) + '\n');
  return new Promise((res) => { const t = setTimeout(() => { console.log(`TIMEOUT id=${myId} ${method}`); res({ timeout: true }); }, 30000); waiters.set(myId, (m) => { clearTimeout(t); res(m); }); });
}
(async () => {
  await send('initialize', { protocolVersion: '2025-06-18', capabilities: { roots: { listChanged: true } }, clientInfo: { name: 'smoke', version: '1' } });
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  await send('tools/list');
  console.log('--- call 1 (A answers, then hands over) ---');
  await send('tools/call', { name: 'ping', arguments: { note: 'c1' } });
  await sleep(1200);
  console.log(`--- after handover: close=${sawClose} exit=${sawExit} stdoutEOF=${sawStdoutEnd} ---`);
  console.log('--- running the "apply": replace VERSION in the install folder ---');
  fs.writeFileSync(`${F}/ho-install/VERSION`, '2.0.0\n');
  fs.writeFileSync(APPLY_DONE, 'done\n');
  await sleep(1500);
  console.log('--- call 2 (must be answered by B, post-update, no re-initialize) ---');
  await send('tools/call', { name: 'ping', arguments: { note: 'c2' } });
  await sleep(500);
  console.log(`--- final: close=${sawClose} exit=${sawExit} stdoutEOF=${sawStdoutEnd} ---`);
  child.stdin.end();
  await sleep(800);
  process.exit(0);
})();
