// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
'use strict';
// Q288 scratch rig: prepares one run directory.
// usage: node setup.js <kind> <spelling|-> <round>
//   kind: user | project | alt | untrusted
const fs = require('fs');
const path = require('path');
const L = require('./lib.js');

function main() {
  const [kind, sp, round] = process.argv.slice(2);
  const runId = kind === 'user' ? `B-user-${sp}-r${round}`
    : kind === 'project' ? `B-proj-${sp}-r${round}`
      : kind === 'alt' ? `C-proj-alt-r${round}`
        : kind === 'untrusted' ? `D-proj-untrusted-r${round}` : null;
  if (!runId) throw new Error('bad kind ' + kind);
  const runDir = path.join(L.ROOT, 'runs', runId);
  if (fs.existsSync(runDir)) throw new Error('run dir exists: ' + runDir);
  const home = path.join(runDir, 'home');
  const logDir = path.join(runDir, 'stublogs');
  fs.mkdirSync(home, { recursive: true });
  fs.mkdirSync(logDir, { recursive: true });
  const setup = { runId, kind, spelling: sp, round: Number(round), adds: [], gets: [], files: {} };
  let spec;

  if (kind === 'user') {
    const neutral = path.join(runDir, 'neutral');
    fs.mkdirSync(neutral, { recursive: true });
    const entries = [L.controlEntry('ctl', logDir), ...L.spellingEntries(sp, logDir)];
    const toml = [L.PROVIDER_TOML, '', ...entries.map((e) => L.serverToml(e) + '\n')].join('\n');
    fs.writeFileSync(path.join(home, 'config.toml'), toml);
    setup.entries = entries;
    spec = { runId, layer: 'user', spelling: sp, round: Number(round), home, cwd: neutral, threadCwd: neutral, logDir, expect: entries.map((e) => e.name) };
  } else {
    const proj = path.join(runDir, 'proj');
    fs.mkdirSync(proj, { recursive: true });
    setup.gitInit = L.run('git', ['init', '-q', proj], { cwd: runDir });
    const dotCodex = path.join(proj, '.codex');
    fs.mkdirSync(dotCodex, { recursive: true });
    const entries = kind === 'project' ? [L.controlEntry('ctl', logDir), ...L.spellingEntries(sp, logDir)]
      : kind === 'alt' ? [L.controlEntry('ctl', logDir), ...L.alternativeEntries(logDir)]
        : [L.controlEntry('ctl', logDir)];
    // The project entries are written the way BrowserAI writes them: by the
    // client's own `codex mcp add`, with CODEX_HOME moved to <repo>\.codex.
    for (const e of entries) {
      const argv = L.addArgs(e);
      setup.adds.push({ name: e.name, intended: e, result: L.codex(argv, dotCodex, proj) });
    }
    for (const e of entries) {
      setup.gets.push({ name: e.name, result: L.codex(['mcp', 'get', e.name, '--json'], dotCodex, proj) });
    }
    setup.files.projectConfigBytesB64 = fs.readFileSync(path.join(dotCodex, 'config.toml')).toString('base64');
    setup.files.projectConfigText = fs.readFileSync(path.join(dotCodex, 'config.toml'), 'utf8');
    setup.files.projectDotCodexListing = fs.readdirSync(dotCodex, { recursive: true });
    const uctl = L.controlEntry('uctl', logDir);
    const trust = kind === 'untrusted' ? '' : `[projects.${L.tomlLiteral(proj)}]\ntrust_level = "trusted"\n`;
    const toml = [L.PROVIDER_TOML, '', L.serverToml(uctl), '', trust].join('\n');
    fs.writeFileSync(path.join(home, 'config.toml'), toml);
    setup.entries = [uctl, ...entries];
    spec = { runId, layer: kind === 'untrusted' ? 'project-untrusted' : 'project', spelling: kind === 'project' ? sp : kind, round: Number(round), home, cwd: proj, threadCwd: proj, logDir, expect: [uctl, ...entries].map((e) => e.name) };
  }
  setup.files.userConfigText = fs.readFileSync(path.join(home, 'config.toml'), 'utf8');
  fs.writeFileSync(path.join(runDir, 'setup.json'), JSON.stringify(setup, null, 1));
  fs.writeFileSync(path.join(runDir, 'spec.json'), JSON.stringify(spec, null, 1));
  const addFailures = setup.adds.filter((a) => a.result.status !== 0).map((a) => a.name);
  console.log(`${runId}: prepared${setup.adds.length ? `, ${setup.adds.length} mcp add, failures=${JSON.stringify(addFailures)}` : ''}`);
  console.log(runDir);
}

main();
