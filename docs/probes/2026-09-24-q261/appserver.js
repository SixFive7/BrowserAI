// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Drives `codex app-server` over stdio JSON-RPC.
// Sequence is given as a JSON array on argv[2]; each step is {m:method, p:params} or
// {sleep:ms} or {marker:"..."}. Responses and notifications are logged.
const { spawn } = require('child_process');
const fs = require('fs');

const CODEX = process.env.CODEX_EXE;
const LOG = process.env.DRIVER_LOG;
const steps = JSON.parse(process.argv[2]);
const log = (s) => fs.appendFileSync(LOG, `${new Date().toISOString()} ${s}\n`);

const child = spawn(CODEX, ['app-server', '--listen', 'stdio://'], {
  stdio: ['pipe', 'pipe', 'pipe'],
  env: process.env,
});
let buf = '';
const pending = new Map();
let nextId = 1;
const notes = [];

child.stdout.on('data', (d) => {
  buf += d.toString('utf8');
  let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim();
    buf = buf.slice(nl + 1);
    if (!line) continue;
    let msg;
    try { msg = JSON.parse(line); } catch (e) { log(`NONJSON ${line.slice(0, 200)}`); continue; }
    if (msg.id !== undefined && (msg.result !== undefined || msg.error !== undefined)) {
      log(`RESP id=${msg.id} ${JSON.stringify(msg.error ?? msg.result).slice(0, 900)}`);
      const p = pending.get(msg.id);
      if (p) { pending.delete(msg.id); p(msg); }
    } else if (msg.method) {
      notes.push(msg);
      log(`NOTE ${msg.method} ${JSON.stringify(msg.params ?? {}).slice(0, 700)}`);
      // auto-answer server->client requests so the server does not block
      if (msg.id !== undefined) {
        const reply = { jsonrpc: '2.0', id: msg.id, result: {} };
        child.stdin.write(JSON.stringify(reply) + '\n');
        log(`AUTOREPLY to ${msg.method}`);
      }
    }
  }
});
child.stderr.on('data', (d) => log(`STDERR ${d.toString('utf8').trim().slice(0, 500)}`));

function call(method, params, timeoutMs = 120000) {
  const id = nextId++;
  const req = { jsonrpc: '2.0', id, method, params };
  log(`SEND id=${id} ${method} ${JSON.stringify(params ?? {}).slice(0, 400)}`);
  child.stdin.write(JSON.stringify(req) + '\n');
  return new Promise((resolve) => {
    const t = setTimeout(() => { log(`TIMEOUT id=${id} ${method}`); pending.delete(id); resolve({ timeout: true }); }, timeoutMs);
    pending.set(id, (m) => { clearTimeout(t); resolve(m); });
  });
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

(async () => {
  const ctx = {};
  for (const step of steps) {
    if (step.sleep) { await sleep(step.sleep); continue; }
    if (step.marker) { log(`MARKER ${step.marker}`); continue; }
    if (step.touch) { const t = fs.readFileSync(step.touch, 'utf8'); fs.writeFileSync(step.touch, t + `
# touched ${Date.now()}
`); log(`TOUCHED ${step.touch}`); continue; }
    let params = JSON.parse(JSON.stringify(step.p ?? {}));
    const subst = (o) => {
      for (const k of Object.keys(o)) {
        if (typeof o[k] === 'string' && o[k] === '$THREAD') o[k] = ctx.threadId;
        else if (o[k] && typeof o[k] === 'object') subst(o[k]);
      }
    };
    subst(params);
    const r = await call(step.m, params, step.timeout ?? 120000);
    if (step.m === 'thread/start' && r.result) {
      ctx.threadId = r.result.thread_id ?? r.result.threadId ?? (r.result.thread && (r.result.thread.id));
      log(`THREAD_ID=${ctx.threadId}`);
    }
    if (step.after) await sleep(step.after);
  }
  await sleep(1500);
  // Added 2026-09-24 for the update-effect arm: the app-server's own exit is
  // logged, so a reader can tell "it left on its own after the EOF" from "the
  // kill below took it", and the kill waits DRIVER_KILL_AFTER_MS (default 2000,
  // which is what every earlier run of this rig used).
  child.on('exit', (code, signal) => log(`APPSERVER EXIT code=${code} signal=${signal}`));
  log('DONE');
  child.stdin.end();
  const killAfter = Number(process.env.DRIVER_KILL_AFTER_MS || 2000);
  setTimeout(() => { log('KILLING the app-server'); child.kill(); process.exit(0); }, killAfter);
})();
