// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A stdio pass-through in front of the real BrowserAI server that records every
// frame WHOLE in both directions, so the byte-exact texts can be read back.
// Derived from docs/probes/2026-09-24-q261/shim.js (sha256 3c42f90b...) with the
// die-after logic removed: this one never ends the server on its own account. When
// the client closes its end, the server's stdin is closed too and the server is
// given 5 s to leave by itself before it is ended through the handle of the
// process this file started (never by name).
//   PT_SERVER, PT_LOGDIR, PT_TAG
'use strict';
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const SERVER = process.env.PT_SERVER;
const LOGDIR = process.env.PT_LOGDIR || '.';
const TAG = process.env.PT_TAG || 'passthru';
fs.mkdirSync(LOGDIR, { recursive: true });

let launchNo = 0;
for (let n = 1; n < 1000; n++) {
  try { const fd = fs.openSync(path.join(LOGDIR, `${TAG}.launch.${n}`), 'wx'); fs.writeSync(fd, `${new Date().toISOString()} shim=${process.pid}\n`); fs.closeSync(fd); launchNo = n; break; }
  catch (e) { if (e.code !== 'EEXIST') throw e; }
}
const LAUNCHES = path.join(LOGDIR, `${TAG}.launches.log`);
const WIRE = path.join(LOGDIR, `${TAG}.wire.jsonl`);
const note = (s) => fs.appendFileSync(LAUNCHES, `${new Date().toISOString()} launch=${launchNo} shim=${process.pid} ${s}\n`);
const wire = (direction, frame) => fs.appendFileSync(WIRE, JSON.stringify({ at: new Date().toISOString(), launch: launchNo, shim: process.pid, direction, frame }) + '\n');

const child = spawn(SERVER, [], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true, env: process.env });
note(`LAUNCH server=${child.pid} exe=${SERVER} BROWSERAI_ROOT=${process.env.BROWSERAI_ROOT}`);

function forward(from, to, direction) {
  let buffer = '';
  from.on('data', (chunk) => {
    to.write(chunk);
    buffer += chunk.toString('utf8');
    let cut;
    while ((cut = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, cut).trim();
      buffer = buffer.slice(cut + 1);
      if (!line) continue;
      try { wire(direction, JSON.parse(line)); } catch (e) { wire(`${direction}-unparsed`, line); }
    }
  });
}
forward(process.stdin, child.stdin, 'client->server');
forward(child.stdout, process.stdout, 'server->client');
child.stderr.on('data', (chunk) => fs.appendFileSync(path.join(LOGDIR, `${TAG}.server.stderr.${launchNo}.log`), chunk));

child.on('exit', (code, signal) => {
  note(`server=${child.pid} exited code=${code} signal=${signal}`);
  fs.writeFileSync(path.join(LOGDIR, `${TAG}.exited.${launchNo}`), `${new Date().toISOString()} server=${child.pid} code=${code}\n`);
  // Let the last frames reach the client, then close this end too.
  setTimeout(() => { note(`EXIT shim=${process.pid}`); process.exit(0); }, 150);
});

process.stdin.on('end', () => {
  note(`client closed its end; closing server=${child.pid}'s stdin`);
  try { child.stdin.end(); } catch (e) { /* ignore */ }
  setTimeout(() => { if (child.exitCode === null) { note(`server=${child.pid} still running 5 s after its stdin closed; ending it by its handle`); try { child.kill(); } catch (e) { /* ignore */ } } }, 5000);
});
