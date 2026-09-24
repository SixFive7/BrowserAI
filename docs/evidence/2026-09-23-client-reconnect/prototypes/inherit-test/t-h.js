// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

const fs = require('fs');
let n = 0;
const t = setInterval(() => {
  n += 1;
  try { fs.writeSync(1, `H-ALIVE ${n} pid=${process.pid}\n`); } catch (e) { fs.appendFileSync('h-err.txt', String(e) + '\n'); }
  if (n >= 5) { clearInterval(t); process.exit(0); }
}, 400);
