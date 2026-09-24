// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

const fs = require('fs');
try { fs.writeSync(1, `H-IMMEDIATE pid=${process.pid}\n`); fs.appendFileSync('h2-log.txt', 'write ok\n'); }
catch (e) { fs.appendFileSync('h2-log.txt', 'write FAILED: ' + String(e) + '\n'); }
try { const st = fs.fstatSync(1); fs.appendFileSync('h2-log.txt', `fd1 isFIFO=${st.isFIFO()} isFile=${st.isFile()} isChar=${st.isCharacterDevice()}\n`); }
catch (e) { fs.appendFileSync('h2-log.txt', 'fstat FAILED: ' + String(e) + '\n'); }
setTimeout(() => { try { fs.writeSync(1, 'H-LATE\n'); fs.appendFileSync('h2-log.txt', 'late write ok\n'); } catch (e) { fs.appendFileSync('h2-log.txt', 'late write FAILED: ' + String(e) + '\n'); } process.exit(0); }, 800);
