// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
'use strict';
// Q288 scratch rig: one `codex app-server` session against a prepared run
// directory. usage: node appsrv.js <runDir>
// <runDir>/spec.json: { runId, layer, home, cwd, threadCwd, expect: [names] }
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const L = require('./lib.js');

const TERMINAL = new Set(['ready', 'failed', 'cancelled']);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function main() {
  const runDir = process.argv[2];
  const spec = JSON.parse(fs.readFileSync(path.join(runDir, 'spec.json'), 'utf8'));
  const logPath = path.join(runDir, 'driver.log');
  const log = (s) => fs.appendFileSync(logPath, `${new Date().toISOString()} ${s}\n`);
  const result = { spec, codex: L.CODEX, startedAt: new Date().toISOString(), notes: [], responses: {}, procs: {}, toolCalls: {}, killed: [] };
  const stubPaths = [L.STUB_ABS, L.STUB_SPACE_ABS];
  result.procs.before = L.procsByPath(stubPaths);

  const env = L.baseEnv({
    CODEX_HOME: spec.home,
    Q288_STUB_KEY: 'not-a-real-key',
    RUST_LOG: 'codex_rmcp_client=debug,codex_mcp=debug,warn',
  });
  const child = spawn(L.CODEX, ['app-server', '--listen', 'stdio://'], { cwd: spec.cwd, env, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
  result.appServerPid = child.pid;
  log(`SPAWNED app-server pid=${child.pid} cwd=${spec.cwd} CODEX_HOME=${spec.home}`);
  let stderrBuf = '';
  child.stderr.on('data', (d) => { stderrBuf += d.toString('utf8'); });
  let exitInfo = null;
  const exited = new Promise((res) => child.on('exit', (code, signal) => { exitInfo = { code, signal, at: new Date().toISOString() }; log(`APPSERVER EXIT code=${code} signal=${signal}`); res(); }));

  let buf = '';
  const pending = new Map();
  let nextId = 1;
  const statusByName = {};
  child.stdout.on('data', (d) => {
    buf += d.toString('utf8');
    let nl;
    while ((nl = buf.indexOf('\n')) >= 0) {
      const line = buf.slice(0, nl).trim();
      buf = buf.slice(nl + 1);
      if (!line) continue;
      let msg;
      try { msg = JSON.parse(line); } catch (e) { log(`NONJSON ${line.slice(0, 300)}`); continue; }
      if (msg.id !== undefined && (msg.result !== undefined || msg.error !== undefined)) {
        log(`RESP id=${msg.id} ${JSON.stringify(msg.error ?? msg.result).slice(0, 3000)}`);
        const p = pending.get(msg.id);
        if (p) { pending.delete(msg.id); p(msg); }
      } else if (msg.method) {
        result.notes.push({ at: new Date().toISOString(), method: msg.method, params: msg.params });
        log(`NOTE ${msg.method} ${JSON.stringify(msg.params ?? {}).slice(0, 2000)}`);
        if (msg.method === 'mcpServer/startupStatus/updated' && msg.params && msg.params.name) {
          statusByName[msg.params.name] = msg.params.status;
        }
        if (msg.id !== undefined) {
          child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: msg.id, result: {} }) + '\n');
          log(`AUTOREPLY ${msg.method}`);
        }
      }
    }
  });

  function call(method, params, timeoutMs) {
    const id = nextId++;
    log(`SEND id=${id} ${method} ${JSON.stringify(params ?? {}).slice(0, 600)}`);
    child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
    return new Promise((resolve) => {
      const t = setTimeout(() => { pending.delete(id); log(`TIMEOUT id=${id} ${method}`); resolve({ timeout: true }); }, timeoutMs || 60000);
      pending.set(id, (m) => { clearTimeout(t); resolve(m); });
    });
  }

  result.responses.initialize = await call('initialize', { clientInfo: { name: 'q288-probe', title: 'q288', version: '1' } }, 30000);
  const ts = await call('thread/start', { cwd: spec.threadCwd }, 60000);
  result.responses.threadStart = ts;
  const threadId = ts && ts.result ? (ts.result.thread && ts.result.thread.id) || ts.result.threadId : null;
  result.threadId = threadId;

  // Wait until every configured name has a terminal startup status.
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    if (spec.expect.every((n) => TERMINAL.has(statusByName[n]))) break;
    await sleep(100);
  }
  result.startupStatus = Object.fromEntries(spec.expect.map((n) => [n, statusByName[n] || null]));
  result.startupStatusAll = Object.assign({}, statusByName);
  await sleep(300);
  result.procs.whileAlive = L.procsByPath(stubPaths);

  if (threadId) {
    result.responses.statusListThread = await call('mcpServerStatus/list', { threadId }, 60000);
  }
  result.responses.statusListNoThread = await call('mcpServerStatus/list', {}, 60000);

  if (threadId) {
    for (const name of Object.keys(statusByName).sort()) {
      if (statusByName[name] !== 'ready') continue;
      result.toolCalls[name] = await call('mcpServer/tool/call', { threadId, server: name, tool: 'probe_whoami', arguments: {} }, 30000);
    }
  }
  result.procs.afterCalls = L.procsByPath(stubPaths);

  log('DONE, closing app-server stdin');
  child.stdin.end();
  await Promise.race([exited, sleep(10000)]);
  if (!exitInfo) {
    log('KILLING the app-server (it did not exit within 10 s of its stdin EOF)');
    result.killed.push({ what: 'app-server', pid: child.pid });
    child.kill();
    await Promise.race([exited, sleep(5000)]);
  }
  result.appServerExit = exitInfo;
  await sleep(500);
  result.procs.afterExit = L.procsByPath(stubPaths);
  for (const p of result.procs.afterExit.list || []) {
    // Anything left is a stub this run's Codex started, so it is ours to end,
    // and it is ended by pid.
    log(`LEFTOVER stub pid=${p.pid} path=${p.path}; ending it by pid`);
    try { process.kill(p.pid); result.killed.push({ what: 'stub', pid: p.pid }); } catch (e) { result.killed.push({ what: 'stub', pid: p.pid, error: String(e) }); }
  }
  if ((result.procs.afterExit.list || []).length) { await sleep(500); result.procs.afterCleanup = L.procsByPath(stubPaths); }

  fs.writeFileSync(path.join(runDir, 'appserver.stderr.txt'), L.stripAnsi(stderrBuf));
  result.mcpListJson = L.codex(['mcp', 'list', '--json'], spec.home, spec.cwd);
  result.stubLogs = L.readStubLogs(spec.logDir);
  result.finishedAt = new Date().toISOString();
  fs.writeFileSync(path.join(runDir, 'result.json'), JSON.stringify(result, null, 1));
  console.log(`${spec.runId}: status=${JSON.stringify(result.startupStatus)} alive=${(result.procs.whileAlive.list || []).length} exit=${JSON.stringify(exitInfo)} leftover=${(result.procs.afterExit.list || []).length}`);
}

main().catch((e) => { console.error('DRIVER FAILURE', e && e.stack || e); process.exit(2); });
