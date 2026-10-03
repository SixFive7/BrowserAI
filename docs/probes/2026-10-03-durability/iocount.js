// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Scratch rig (durability research, 2026-10-03). What a job object's I/O counters count: after a
// GO line on stdin, write 32 MiB to stdout (a pipe the parent reads), or to a file, or nothing.
'use strict';
const fs = require('node:fs');
const [mode, file] = process.argv.slice(2);
const chunk = Buffer.alloc(64 * 1024, 'x');
chunk[chunk.length - 1] = 10;
process.stdin.once('data', async () => {
  if (mode === 'pipe') {
    for (let i = 0; i < 512; i++) {
      if (!process.stdout.write(chunk)) await new Promise(r => process.stdout.once('drain', r));
    }
  } else if (mode === 'file') {
    const fd = fs.openSync(file, 'w');
    for (let i = 0; i < 512; i++) fs.writeSync(fd, chunk);
    fs.closeSync(fd);
  }
  process.stderr.write('DONE\n');
  setTimeout(() => process.exit(0), 200);
});
