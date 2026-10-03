// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// batch.js -- runs scenario runs one after another, never two at once.
// node batch.js <planFile>   (plan: one line per run: scenario version browser tag [variant])
'use strict';
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\state-across-close';
const plan = fs.readFileSync(process.argv[2], 'utf8').split(/\r?\n/).map(l => l.trim()).filter(l => l && !l.startsWith('#'));
const log = path.join(ROOT, 'batch-progress.txt');
const say = s => fs.appendFileSync(log, `${new Date().toISOString()} ${s}\n`);
const PER_RUN_MS = 8 * 60 * 1000;

(async () => {
  say(`BATCH START ${process.argv[2]} runs=${plan.length} pid=${process.pid}`);
  for (const line of plan) {
    const args = line.split(/\s+/);
    const t0 = Date.now();
    const out = fs.openSync(path.join(ROOT, 'runs', `_stdout-${args.join('-')}.txt`), 'w');
    const p = spawn(process.execPath, [path.join(ROOT, 'rig', 'scenarios.js'), ...args], { stdio: ['ignore', out, out], windowsHide: true });
    say(`START ${line} pid=${p.pid}`);
    const code = await new Promise(res => {
      const timer = setTimeout(() => { say(`TIMEOUT ${line} -- terminating pid ${p.pid} (our own child)`); p.kill(); }, PER_RUN_MS);
      p.on('exit', c => { clearTimeout(timer); res(c); });
    });
    fs.closeSync(out);
    say(`END ${line} exit=${code} ${Math.round((Date.now() - t0) / 1000)}s`);
    await new Promise(r => setTimeout(r, 1500));
  }
  say('BATCH END');
})();
