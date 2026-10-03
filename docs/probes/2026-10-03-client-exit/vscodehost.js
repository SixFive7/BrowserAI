// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Stands in for the VS Code extension host's Agent SDK transport, as read in
// anthropic.claude-code-2.1.287-win32-x64/extension.js (offsets in code/ext-2.1.287-*.txt):
//   spawn(claude, args, {stdio:["pipe","pipe","pipe"], signal, env, windowsHide:true})   (spawnLocalProcess)
//   close(): processStdin.end(); if still running: setTimeout 2000 -> on win32 setTimeout 5000 -> kill("SIGKILL")
//   process "exit" handler (BI.killAll): on win32 only stdin.end() for every tracked child
// usage: node vscodehost.js <claude.exe> <argsJson> <mode: sdk-close|host-exit> <logfile>
const { spawn } = require('child_process');
const fs = require('fs');
const [exe, argsJson, mode, logFile] = process.argv.slice(2);
const args = JSON.parse(argsJson);
const log = (s) => fs.appendFileSync(logFile, `${new Date().toISOString()} ${s}\n`);
const initReq = '{"type":"control_request","request_id":"req_init_1","request":{"subtype":"initialize"}}';
const userMsg = JSON.stringify({ type: 'user', session_id: '', message: { role: 'user', content: [{ type: 'text', text: 'Call the probe ping tool, then say done.' }] }, parent_tool_use_id: null });

const child = spawn(exe, args, { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true, env: process.env });
log(`SPAWNED pid=${child.pid} mode=${mode}`);
child.stderr.on('data', (d) => log('ERR ' + d.toString().trim().slice(0, 300)));
child.on('exit', (code, signal) => log(`CHILD_EXIT code=${code} signal=${signal}`));
let buf = '';
let sentUser = false;
let ended = false;
child.stdout.on('data', (d) => {
  buf += d.toString('utf8');
  let i;
  while ((i = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, i);
    buf = buf.slice(i + 1);
    if (line.includes('"type":"control_response"') && !sentUser) {
      sentUser = true;
      child.stdin.write(userMsg + '\n');
      log('SENT user message');
    }
    if (line.includes('"type":"result"') && !ended) {
      ended = true;
      log('RESULT seen; ending in 2000 ms');
      setTimeout(end, 2000);
    }
  }
});

function end() {
  if (mode === 'host-exit') {
    // The extension host going away: its exit handler ends stdin of every tracked child on
    // win32 (no kill), and then the host process is gone.
    log('HOST_EXIT: stdin.end() then process.exit(0)');
    try { child.stdin.end(); } catch (e) { }
    process.exit(0);
  }
  // sdk-close: ProcessTransport.close() on win32.
  log('SDK_CLOSE: stdin.end()');
  child.stdin.end();
  setTimeout(() => {
    if (child.exitCode !== null || child.signalCode != null) return;
    setTimeout(() => {
      if (child.exitCode === null) { log('SDK_CLOSE: SIGKILL after 2000+5000 ms'); child.kill('SIGKILL'); }
    }, 5000);
  }, 2000);
  child.on('exit', () => setTimeout(() => { log('DRIVER_EXIT'); process.exit(0); }, 1000));
}
child.stdin.write(initReq + '\n');
log('SENT initialize');
setTimeout(() => { log('DRIVER_TIMEOUT'); process.exit(3); }, 120000);
