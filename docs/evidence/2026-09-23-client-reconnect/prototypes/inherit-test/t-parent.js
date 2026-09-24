// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

const { spawn } = require('child_process');
const c = spawn(process.execPath, [__dirname + '/t-a3.js'], { stdio: ['pipe', 'pipe', 'pipe'] });
c.stdout.on('data', (d) => process.stdout.write('OUT: ' + d.toString()));
c.stdout.on('end', () => console.log(`EVENT stdout-EOF at +${Date.now() - t0}ms`));
c.on('exit', (code) => console.log(`EVENT exit code=${code} at +${Date.now() - t0}ms`));
c.on('close', () => console.log(`EVENT close at +${Date.now() - t0}ms`));
const t0 = Date.now();
setTimeout(() => { console.log('--- parent done waiting ---'); process.exit(0); }, 4000);
