// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

const fs = require('fs');
const L = __dirname + '/s-h-log.txt';
const log = (s) => fs.appendFileSync(L, `${new Date().toISOString()} H pid=${process.pid} ${s}\n`);
log('start, attaching stdin');
process.stdin.on('data', (d) => { log(`STDIN DATA ${JSON.stringify(d.toString())}`); fs.writeSync(1, `H-ECHO ${d.toString().trim()}\n`); });
process.stdin.on('end', () => log('STDIN END'));
process.stdin.on('error', (e) => log('STDIN ERROR ' + e.message));
setTimeout(() => { log('done'); process.exit(0); }, 3000);
