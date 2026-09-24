// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr


// H: the helper. Lives OUTSIDE the install folder. It holds the client's pipe open
// while the "apply" replaces the server on disk, then starts B with the same
// inherited handles and exits. It deliberately does NOT read stdin: requests the
// client sends during the apply sit in the OS pipe buffer and B reads them.
const fs = require('fs');
const { spawn } = require('child_process');
const { mkLog } = require('./common.js');
const LOGDIR = process.env.HO_LOGDIR;
const TAG = process.env.HO_TAG || 'ho';
const STATE = process.env.HO_STATE;
const APPLY_DONE = process.env.HO_APPLY_DONE;     // marker the "apply" touches when finished
const HDIR = process.env.HO_HELPER_DIR;
const INSTALL = process.env.HO_INSTALL_DIR;       // where B is started from after the swap
const NODE = process.execPath;
const WAIT_MAX_MS = Number(process.env.HO_WAIT_MAX_MS || 60000);
const log = mkLog(LOGDIR, TAG);
log('LAUNCH', 'role=H holding the pipe; not reading stdin');
process.on('exit', (c) => { try { log('EXIT', `role=H code=${c}`); } catch (e) {} });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
(async () => {
  const t0 = Date.now();
  while (Date.now() - t0 < WAIT_MAX_MS) {
    if (fs.existsSync(APPLY_DONE)) { log('APPLY-DONE', `after ${Date.now() - t0}ms`); break; }
    await sleep(50);
  }
  if (!fs.existsSync(APPLY_DONE)) { log('APPLY-TIMEOUT', `${WAIT_MAX_MS}ms; starting B anyway`); }
  const b = spawn(NODE, [INSTALL + '/b.js'], {
    stdio: ['inherit', 'inherit', 'inherit'],
    env: process.env,
    windowsHide: true,
    detached: true,            // MUST: a non-detached child dies with H's job object
  });
  b.unref();
  log('B-SPAWNED', `pid=${b.pid} from ${INSTALL}/b.js with inherited stdio`);
  await sleep(150);
  log('H-EXIT', 'handing the pipe to B and exiting 0');
  process.exit(0);
})();
