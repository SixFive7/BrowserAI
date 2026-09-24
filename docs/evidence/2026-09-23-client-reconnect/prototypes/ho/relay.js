// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// R: a permanent relay. The client spawns R and R never exits for the session.
// R holds both client pipes for the whole session and forwards to a child server S
// that it re-spawns across an "apply". The client sees one process that never dies.
const fs = require('fs');
const { spawn } = require('child_process');
const path = require('path');

const LOGDIR = process.env.HO_LOGDIR;
const TAG = process.env.HO_TAG || 'relay';
const INSTALL = process.env.HO_INSTALL_DIR;
const SERVER = process.env.HO_SERVER_FILE || 's.js';
const SWAP_REQ = process.env.HO_SWAP_REQUEST;
const APPLY_DONE = process.env.HO_APPLY_DONE;
const SWAP_DONE = process.env.HO_SWAP_DONE;
const NODE = process.execPath;
let swapsDone = 0;
fs.mkdirSync(LOGDIR, { recursive: true });
const LF = path.join(LOGDIR, TAG + '.handover.log');
const log = (k, d) => fs.appendFileSync(LF, new Date().toISOString() + ' ' + process.pid + ' R ' + k + ' ' + d + '\n');
log('LAUNCH', 'relay holding the client pipes; server dir=' + INSTALL);
process.on('exit', (c) => { try { log('EXIT', 'code=' + c); } catch (e) {} });

let child = null;
let childBuf = '';
let clientBuf = '';
let swapping = false;
const queue = [];
let initFrame = null;
let initializedFrame = null;
const inFlight = new Map();
let generation = 0;

function toClient(line) { fs.writeSync(1, line + '\n'); }

function startChild() {
  generation += 1;
  const gen = generation;
  child = spawn(NODE, [path.join(INSTALL, SERVER)], { stdio: ['pipe', 'pipe', 'pipe'], env: process.env, windowsHide: true });
  log('CHILD-SPAWNED', 'gen=' + gen + ' pid=' + child.pid + ' from ' + path.join(INSTALL, SERVER));
  childBuf = '';
  child.stdout.on('data', (d) => {
    childBuf += d.toString('utf8');
    let nl;
    while ((nl = childBuf.indexOf('\n')) >= 0) {
      const line = childBuf.slice(0, nl).trim(); childBuf = childBuf.slice(nl + 1);
      if (!line) continue;
      let m; try { m = JSON.parse(line); } catch (e) { log('CHILD-BADLINE', line.slice(0, 160)); continue; }
      if (m.id !== undefined && m.id === '__relay_init__') { log('CHILD-REPLAY-INIT-OK', 'gen=' + gen); continue; }
      if (m.id !== undefined) inFlight.delete(m.id);
      toClient(line);
    }
  });
  child.stderr.on('data', (d) => log('CHILD-STDERR', d.toString().trim().slice(0, 200)));
  child.on('exit', (code) => { log('CHILD-EXIT', 'gen=' + gen + ' code=' + code + ' swapping=' + swapping); });
  return child;
}

function toChild(line) { try { child.stdin.write(line + '\n'); } catch (e) { log('TO-CHILD-FAILED', String(e)); } }

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function swap() {
  if (swapping) return;
  swapping = true;
  log('SWAP-BEGIN', 'queueing client frames; in-flight=' + inFlight.size);
  const old = child;
  try { old.stdin.end(); } catch (e) {}
  const t0 = Date.now();
  while (old.exitCode === null && Date.now() - t0 < 10000) await sleep(50);
  log('SWAP-OLD-GONE', 'exitCode=' + old.exitCode + ' after ' + (Date.now() - t0) + 'ms');
  const t1 = Date.now();
  while (!fs.existsSync(APPLY_DONE) && Date.now() - t1 < 60000) await sleep(50);
  log('SWAP-APPLY-DONE', 'after ' + (Date.now() - t1) + 'ms');
  startChild();
  if (initFrame) {
    toChild(initFrame.replace(/"id":\s*[^,}]+/, '"id":"__relay_init__"'));
    log('SWAP-REPLAY', 'initialize replayed to the new S');
  }
  if (initializedFrame) toChild(initializedFrame);
  await sleep(250);
  for (const [id, frame] of inFlight) { log('SWAP-RESEND', 'in-flight id=' + id); toChild(frame); }
  while (queue.length) toChild(queue.shift());
  swapping = false;
  swapsDone += 1;
  try { if (SWAP_REQ) fs.unlinkSync(SWAP_REQ); } catch (e) {}
  try { if (SWAP_DONE) fs.writeFileSync(SWAP_DONE, String(swapsDone)); } catch (e) {}
  log('SWAP-END', 'forwarding resumed; the client saw nothing; swaps=' + swapsDone);
}

process.stdin.on('data', (d) => {
  clientBuf += d.toString('utf8');
  let nl;
  while ((nl = clientBuf.indexOf('\n')) >= 0) {
    const line = clientBuf.slice(0, nl).trim(); clientBuf = clientBuf.slice(nl + 1);
    if (!line) continue;
    let m; try { m = JSON.parse(line); } catch (e) { log('CLIENT-BADLINE', line.slice(0, 160)); continue; }
    if (m.method === 'initialize') initFrame = line;
    if (m.method === 'notifications/initialized') initializedFrame = line;
    if (m.id !== undefined && m.method) inFlight.set(m.id, line);
    if (swapping) { queue.push(line); log('QUEUED', m.method + ' id=' + m.id); continue; }
    toChild(line);
    if (SWAP_REQ && fs.existsSync(SWAP_REQ) && !swapping) { log('SWAP-REQUESTED', 'marker present'); swap(); }
  }
});
process.stdin.on('end', () => {
  log('CLIENT-STDIN-END', 'client closed; shutting the child down');
  try { child.stdin.end(); } catch (e) {}
  setTimeout(() => { try { child.kill(); } catch (e) {} process.exit(0); }, 500);
});

startChild();

// A swap is requested by a marker file, polled on a timer, so it needs no client
// traffic to notice: this stands in for the "an update is staged" signal.
if (SWAP_REQ) {
  setInterval(() => {
    if (!swapping && fs.existsSync(SWAP_REQ)) { log('SWAP-REQUESTED', 'marker seen by poller'); swap(); }
  }, 200);
}
