// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
'use strict';
// Q288 scratch rig: what `codex mcp add` writes when a SHELL sits between the
// person and Codex, three rounds. BrowserAI itself passes argv with no shell;
// this is the manual line a person pastes into cmd.exe.
// usage: node cmdline.js
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const L = require('./lib.js');

const out = [];
for (const r of [1, 2, 3]) {
  const runDir = path.join(L.ROOT, 'runs', `A-cmdline-r${r}`);
  const home = path.join(runDir, 'home');
  fs.mkdirSync(home, { recursive: true });
  const line = `""${L.CODEX}" mcp add viacmd -- %LOCALAPPDATA%\\codex-expansion-probe\\probe-stub.exe %LOCALAPPDATA%\\codex-expansion-probe\\arg-marker"`;
  const res = spawnSync('cmd.exe', ['/d', '/s', '/c', line], {
    env: L.baseEnv({ CODEX_HOME: home }), windowsHide: true, windowsVerbatimArguments: true, encoding: 'utf8', timeout: 60000,
  });
  const cfg = fs.existsSync(path.join(home, 'config.toml')) ? fs.readFileSync(path.join(home, 'config.toml'), 'utf8') : null;
  const get = L.codex(['mcp', 'get', 'viacmd', '--json'], home, runDir);
  const rec = { round: r, cmdLine: line, status: res.status, stdout: L.stripAnsi(res.stdout).trim(), stderr: L.stripAnsi(res.stderr).trim().slice(0, 400), config: cfg, get: get.stdout.trim() };
  fs.writeFileSync(path.join(runDir, 'result.json'), JSON.stringify(rec, null, 1));
  out.push(rec);
  console.log(`A-cmdline-r${r}: exit=${res.status} config=${JSON.stringify(cfg)}`);
}
