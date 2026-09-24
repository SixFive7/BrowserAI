// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

const fs = require('fs');
const { spawn } = require('child_process');
fs.writeSync(1, `A-ALIVE pid=${process.pid}\n`);
const DET = process.env.DETACH === '1';
const h = spawn(process.execPath, [__dirname + '/t-h3.js'], { stdio: ['inherit', 'inherit', 'inherit'], windowsHide: true, detached: DET });
h.unref();
fs.writeSync(1, `A-SPAWNED-H pid=${h.pid} detached=${DET}\n`);
setTimeout(() => { fs.writeSync(1, 'A-EXITING\n'); process.exit(0); }, 300);
