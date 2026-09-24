// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

const fs = require('fs');
const { spawn } = require('child_process');
const MODE = process.env.A_STDIN_MODE || 'destroy';   // destroy | pause | nothing
fs.writeSync(1, `A-ALIVE pid=${process.pid} mode=${MODE}\n`);
process.stdin.on('data', (d) => fs.writeSync(1, `A-SAW ${d.toString().trim()}\n`));
const h = spawn(process.execPath, [__dirname + '/s-h.js'], { stdio: ['inherit', 'inherit', 'inherit'], windowsHide: true, detached: true });
h.unref();
fs.writeSync(1, `A-SPAWNED-H pid=${h.pid}\n`);
setTimeout(() => {
  if (MODE === 'destroy') { try { process.stdin.pause(); process.stdin.destroy(); } catch (e) {} }
  else if (MODE === 'pause') { try { process.stdin.pause(); } catch (e) {} }
  fs.writeSync(1, 'A-EXITING\n');
  process.exit(0);
}, 400);
