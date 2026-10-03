// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The stand-in updater, staged the way UpdateInProgressTests stages it
// (tests/BrowserAI.Tests/UpdateInProgressTests.cs:223-240): a copy of cmd.exe at
// <root>\Update.exe, started with /d /q /k and waiting on a standard input nothing writes
// to. The server under test knows it only as a running process whose full image path is
// its install root's Update.exe.
//
// It is ended through the handle of the process this file started, when <logs>/end-the-updater
// appears or after SI_LIFE_MS, whichever is first; then <logs>/updater-ended is written.
//   SI_ROOT, SI_LOGS, SI_LIFE_MS (default 600000)
'use strict';
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const ROOT = process.env.SI_ROOT;
const LOGS = process.env.SI_LOGS;
const LIFE = Number(process.env.SI_LIFE_MS || 600000);
const log = (s) => fs.appendFileSync(path.join(LOGS, 'standin.log'), `${new Date().toISOString()} ${s}\n`);
const exe = path.join(ROOT, 'Update.exe');
if (!fs.existsSync(exe)) fs.copyFileSync(path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'cmd.exe'), exe);
const child = spawn(exe, ['/d', '/q', '/k'], { stdio: ['pipe', 'ignore', 'ignore'], windowsHide: true, cwd: ROOT });
log(`STARTED stand-in pid=${child.pid} image=${exe}`);
fs.writeFileSync(path.join(LOGS, 'updater.pid'), String(child.pid));
const t0 = Date.now();
let done = false;
child.on('exit', (code, signal) => {
  log(`stand-in pid=${child.pid} exited code=${code} signal=${signal} after ${Date.now() - t0} ms`);
  fs.writeFileSync(path.join(LOGS, 'updater-ended'), new Date().toISOString());
  done = true;
  setTimeout(() => process.exit(0), 100);
});
const tick = setInterval(() => {
  if (done) { clearInterval(tick); return; }
  const trigger = fs.existsSync(path.join(LOGS, 'end-the-updater'));
  if (trigger || Date.now() - t0 >= LIFE) {
    clearInterval(tick);
    log(`ENDING stand-in pid=${child.pid} by its handle (${trigger ? 'trigger file' : 'life ' + LIFE + ' ms'})`);
    child.kill();
  }
}, 50);
