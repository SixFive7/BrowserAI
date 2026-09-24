// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

const fs = require('fs');
const L = __dirname + '/h3-log.txt';
const log = (s) => fs.appendFileSync(L, `${new Date().toISOString()} pid=${process.pid} ${s}\n`);
log('start');
let n = 0;
const t = setInterval(() => {
  n += 1;
  try { fs.writeSync(1, `H3 tick ${n}\n`); log(`write ${n} OK`); }
  catch (e) { log(`write ${n} FAILED ${e.code || ''} ${e.message}`); }
  if (n >= 8) { clearInterval(t); log('done'); process.exit(0); }
}, 400);
process.on('exit', (c) => log(`exit code=${c}`));
