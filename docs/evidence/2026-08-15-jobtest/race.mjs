// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Spawns a grandchild as its very first act, then reports both PIDs.
// Used to demonstrate whether a grandchild created before
// AssignProcessToJobObject lands inside the job or outside it.
import { spawn } from 'node:child_process';
import fs from 'node:fs';

const out = process.argv[2];
const g = spawn(process.execPath, ['-e', 'setTimeout(() => {}, 900000)'], { stdio: 'ignore' });
fs.writeFileSync(out, `${g.pid}\n${process.pid}\n`);
setTimeout(() => {}, 900000);
