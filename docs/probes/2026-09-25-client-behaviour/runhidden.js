// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Runs one executable with no window (windowsHide: CREATE_NO_WINDOW for a console child,
// SW_HIDE for a GUI one), both streams drained to files, the environment this process has
// with the named variables set and CLAUDECODE-style session variables removed.
// usage: node runhidden.js <logprefix> <set-json> <exe> [args...]
'use strict';
const { spawn } = require('child_process');
const fs = require('fs');
const [prefix, setJson, exe, ...args] = process.argv.slice(2);
const env = Object.assign({}, process.env, JSON.parse(setJson.startsWith('@') ? fs.readFileSync(setJson.slice(1), 'utf8') : setJson));
for (const k of Object.keys(env)) if (/^(CLAUDECODE|CLAUDE_CODE_ENTRYPOINT|CLAUDE_CODE_SSE_PORT|CLAUDE_CODE_SESSION_ID|CLAUDE_CODE_CHILD_SESSION)$/i.test(k)) delete env[k];
const t0 = Date.now();
const child = spawn(exe, args, { env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
const out = fs.createWriteStream(prefix + '.stdout.txt');
const err = fs.createWriteStream(prefix + '.stderr.txt');
child.stdout.pipe(out); child.stderr.pipe(err);
child.on('error', (e) => { console.log(`SPAWN ERROR ${e.message}`); process.exit(3); });
child.on('close', (code, signal) => {
  const line = `${new Date().toISOString()} ${exe} ${JSON.stringify(args)} exit=${code} signal=${signal} ms=${Date.now() - t0} pid=${child.pid}`;
  fs.appendFileSync(prefix + '.result.txt', line + '\n');
  console.log(line);
  process.exit(0);
});
