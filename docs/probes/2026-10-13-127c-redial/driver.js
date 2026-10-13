// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Starts one Claude Code for the 127 c measurement and sees it through one turn.
// "print" is `claude -p` with the prompt on its command line; "stream" is the
// stream-json transport the VS Code extension drives, one user message written to its
// input and the input closed once its result arrives. The client's environment is this
// process's own, which the run script sets: a scratch CLAUDE_CONFIG_DIR and CODEX_HOME,
// and the local model stub.
//
// Environment: DRIVER_CLAUDE, the client's executable; DRIVER_MODE, "print" or
// "stream"; DRIVER_ARGS, its arguments as a JSON array; DRIVER_CWD, its working folder;
// DRIVER_OUT, the folder its output and this driver's log go to; DRIVER_BOUND_MS, the
// hang detector.

const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const CLAUDE = process.env.DRIVER_CLAUDE;
const MODE = process.env.DRIVER_MODE || 'print';
const ARGS = JSON.parse(process.env.DRIVER_ARGS || '[]');
const CWD = process.env.DRIVER_CWD;
const OUT = process.env.DRIVER_OUT;
const BOUND = Number(process.env.DRIVER_BOUND_MS || 180000);

const log = (line) => fs.appendFileSync(path.join(OUT, 'driver.log'), `${new Date().toISOString()} ${line}\n`);

const child = spawn(CLAUDE, ARGS, { cwd: CWD, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true, env: process.env });

fs.writeFileSync(path.join(OUT, 'client.pid'), String(child.pid));
log(`started pid=${child.pid} mode=${MODE}`);

const stdout = fs.createWriteStream(path.join(OUT, 'client-stdout.log'));
const stderr = fs.createWriteStream(path.join(OUT, 'client-stderr.log'));

child.stderr.pipe(stderr);

let pending = '';

child.stdout.on('data', (chunk) => {
  stdout.write(chunk);

  if (MODE !== 'stream') {
    return;
  }

  pending += chunk.toString('utf8');

  let newline;

  while ((newline = pending.indexOf('\n')) >= 0) {
    const line = pending.slice(0, newline);
    pending = pending.slice(newline + 1);

    if (/"type"\s*:\s*"result"/.test(line)) {
      log('result arrived; closing the client input');
      child.stdin.end();
    }
  }
});

if (MODE === 'stream') {
  const message = {
    type: 'user',
    session_id: '',
    message: { role: 'user', content: [{ type: 'text', text: 'Use the browserai tools.' }] },
    parent_tool_use_id: null,
  };

  child.stdin.write(JSON.stringify(message) + '\n');
} else {
  child.stdin.end();
}

const hang = setTimeout(() => {
  log(`HANG: the client did not end within ${BOUND} ms; ending the process this driver started`);
  child.kill();
}, BOUND);

child.on('exit', (code, signal) => {
  clearTimeout(hang);
  log(`exited code=${code} signal=${signal}`);
  stdout.end();
  stderr.end();
  setTimeout(() => process.exit(0), 200);
});
