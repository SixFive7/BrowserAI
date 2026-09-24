// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
'use strict';
// Q288 scratch rig: shared helpers. Not part of the product.
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const ROOT = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\codex-expansion';
const LAD = process.env.LOCALAPPDATA;
const PROBE_DIR = path.join(LAD, 'codex-expansion-probe');
const STUB_ABS = path.join(PROBE_DIR, 'probe-stub.exe');
const SPACE_LAD = path.join(PROBE_DIR, 'sp ace');
const STUB_SPACE_ABS = path.join(SPACE_LAD, 'codex-expansion-probe', 'probe-stub.exe');

function readCodexPath() {
  const manifest = path.join(LAD, 'OpenAI', 'Codex', 'chrome-native-hosts-v2.json');
  const m = JSON.parse(fs.readFileSync(manifest, 'utf8'));
  for (const e of m.entries || []) {
    const p = e && e.paths && e.paths.codexCliPath;
    if (p && fs.existsSync(p)) return p;
  }
  throw new Error('no codexCliPath in ' + manifest);
}
const CODEX = readCodexPath();

function stripAnsi(s) { return (s || '').replace(/\x1b\[[0-9;?]*[A-Za-z]/g, ''); }

function baseEnv(extra) {
  const env = {};
  for (const [k, v] of Object.entries(process.env)) {
    if (/^CLAUDE/i.test(k)) continue;           // not ours to hand on
    if (/^CODEX_/i.test(k)) continue;           // nothing inherited may steer Codex
    env[k] = v;
  }
  return Object.assign(env, extra || {});
}

function run(file, args, opts) {
  opts = opts || {};
  const t0 = Date.now();
  const r = spawnSync(file, args, {
    env: opts.env, cwd: opts.cwd, windowsHide: true, encoding: 'utf8',
    timeout: opts.timeoutMs || 60000, maxBuffer: 64 * 1024 * 1024,
  });
  return {
    file, args, cwd: opts.cwd || null, status: r.status, signal: r.signal,
    error: r.error ? String(r.error) : null,
    stdout: stripAnsi(r.stdout), stderr: stripAnsi(r.stderr), ms: Date.now() - t0,
  };
}

function codex(args, home, cwd, extraEnv) {
  return run(CODEX, args, { env: baseEnv(Object.assign({ CODEX_HOME: home }, extraEnv || {})), cwd });
}

// Observation only: every process whose image path is one of the stub copies,
// with its parent's image path. Keyed by PATH, never by image name.
function procsByPath(paths) {
  const filter = paths.map((p) => `ExecutablePath = '${p.replace(/\\/g, '\\\\')}'`).join(' OR ');
  const script = [
    "$ErrorActionPreference='Stop'",
    `$ps = @(Get-CimInstance Win32_Process -Filter "${filter}")`,
    '$out = foreach ($p in $ps) {',
    '  $parent = Get-CimInstance Win32_Process -Filter "ProcessId = $($p.ParentProcessId)"',
    "  [pscustomobject]@{ pid=$p.ProcessId; ppid=$p.ParentProcessId; path=$p.ExecutablePath; commandLine=$p.CommandLine; created=$p.CreationDate.ToUniversalTime().ToString('o'); parentPath=$parent.ExecutablePath; parentPpid=$parent.ParentProcessId }",
    '}',
    'ConvertTo-Json -InputObject @($out) -Depth 3 -Compress',
  ].join('\n');
  const r = run('pwsh.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { timeoutMs: 60000 });
  let list = null;
  try { const t = r.stdout.trim(); list = t ? JSON.parse(t) : []; if (!Array.isArray(list)) list = [list]; } catch (e) { list = null; }
  return { at: new Date().toISOString(), list, status: r.status, stderr: r.stderr.trim() };
}

function tomlLiteral(s) {
  if (s.includes("'") || /[\r\n]/.test(s)) throw new Error('not a literal-safe string: ' + s);
  return `'${s}'`;
}

function serverToml(e) {
  const lines = [`[mcp_servers.${e.name}]`, `command = ${tomlLiteral(e.command)}`];
  if (e.args && e.args.length) lines.push(`args = [${e.args.map(tomlLiteral).join(', ')}]`);
  if (e.env && Object.keys(e.env).length) {
    lines.push(`env = { ${Object.entries(e.env).map(([k, v]) => `${k} = ${tomlLiteral(v)}`).join(', ')} }`);
  }
  return lines.join('\n');
}

const PROVIDER_TOML = [
  'model = "stub-model"',
  'model_provider = "stub"',
  'approval_policy = "never"',
  'sandbox_mode = "read-only"',
  '',
  '[model_providers.stub]',
  'name = "stub"',
  'base_url = "http://127.0.0.1:9/v1"',
  'wire_api = "responses"',
  'env_key = "Q288_STUB_KEY"',
].join('\n');

// The four spellings under test, and the prefix each puts in front of
// "/codex-expansion-probe/..." to name the stub under %LOCALAPPDATA%.
const SPELLINGS = {
  braced: { fwd: '${LOCALAPPDATA}', back: '${LOCALAPPDATA}' },
  bare: { fwd: '$LOCALAPPDATA', back: '$LOCALAPPDATA' },
  percent: { fwd: '%LOCALAPPDATA%', back: '%LOCALAPPDATA%' },
  tilde: { fwd: '~/AppData/Local', back: '~\\AppData\\Local' },
};

function probeEnv(logDir, tag, more) {
  return Object.assign({ PROBE_LOG_DIR: logDir, PROBE_TAG: tag }, more || {});
}

function spellingEntries(sp, logDir) {
  const s = SPELLINGS[sp];
  const argMarker = `${s.fwd}/codex-expansion-probe/arg-marker`;
  const envMarker = `${s.fwd}/codex-expansion-probe/env-marker`;
  return [
    { name: `${sp}_cmd`, command: `${s.fwd}/codex-expansion-probe/probe-stub.exe`, args: [argMarker, '--tag', `${sp}_cmd`], env: probeEnv(logDir, `${sp}_cmd`) },
    { name: `${sp}_cmdb`, command: `${s.back}\\codex-expansion-probe\\probe-stub.exe`, args: [argMarker, '--tag', `${sp}_cmdb`], env: probeEnv(logDir, `${sp}_cmdb`) },
    { name: `${sp}_arg`, command: STUB_ABS, args: [argMarker, '--tag', `${sp}_arg`], env: probeEnv(logDir, `${sp}_arg`, { PROBE_SPELLED: envMarker }) },
  ];
}

function controlEntry(name, logDir) {
  return { name, command: STUB_ABS, args: ['--tag', name], env: probeEnv(logDir, name) };
}

function alternativeEntries(logDir) {
  const viaCmd = '%LOCALAPPDATA%\\codex-expansion-probe\\probe-stub.exe';
  const spaceEnv = { LOCALAPPDATA: SPACE_LAD };
  return [
    { name: 'cmdwrap', command: 'cmd.exe', args: ['/d', '/c', viaCmd, '--tag', 'cmdwrap'], env: probeEnv(logDir, 'cmdwrap') },
    { name: 'cmdwrap_v', command: 'cmd.exe', args: ['/d', '/v:on', '/c', '!LOCALAPPDATA!\\codex-expansion-probe\\probe-stub.exe', '--tag', 'cmdwrap_v'], env: probeEnv(logDir, 'cmdwrap_v') },
    { name: 'cmdwrap_space', command: 'cmd.exe', args: ['/d', '/c', viaCmd, '--tag', 'cmdwrap_space'], env: probeEnv(logDir, 'cmdwrap_space', spaceEnv) },
    { name: 'cmdwrap_space_q', command: 'cmd.exe', args: ['/d', '/c', `"${viaCmd}"`, '--tag', 'cmdwrap_space_q'], env: probeEnv(logDir, 'cmdwrap_space_q', spaceEnv) },
    { name: 'cmdwrap_space_qs', command: 'cmd.exe', args: ['/d', '/s', '/c', `"${viaCmd}" --tag cmdwrap_space_qs`], env: probeEnv(logDir, 'cmdwrap_space_qs', spaceEnv) },
    { name: 'cmdwrap_space_v', command: 'cmd.exe', args: ['/d', '/v:on', '/c', '!LOCALAPPDATA!\\codex-expansion-probe\\probe-stub.exe', '--tag', 'cmdwrap_space_v'], env: probeEnv(logDir, 'cmdwrap_space_v', spaceEnv) },
    { name: 'barepath', command: 'probe-stub.exe', args: ['--tag', 'barepath'], env: probeEnv(logDir, 'barepath', { PATH: PROBE_DIR }) },
    { name: 'tildepath', command: 'probe-stub.exe', args: ['--tag', 'tildepath'], env: probeEnv(logDir, 'tildepath', { PATH: '~\\AppData\\Local\\codex-expansion-probe' }) },
  ];
}

// `codex mcp add` argv for an entry, exactly as a no-shell caller passes it.
function addArgs(e) {
  const a = ['mcp', 'add', e.name];
  for (const [k, v] of Object.entries(e.env || {})) a.push('--env', `${k}=${v}`);
  a.push('--', e.command, ...(e.args || []));
  return a;
}

function readStubLogs(dir) {
  const out = [];
  if (!fs.existsSync(dir)) return out;
  for (const f of fs.readdirSync(dir).sort()) {
    if (!f.endsWith('.jsonl')) continue;
    const events = fs.readFileSync(path.join(dir, f), 'utf8').split('\n').filter(Boolean).map((l) => { try { return JSON.parse(l); } catch { return { unparsed: l }; } });
    out.push({ file: f, events });
  }
  return out;
}

module.exports = {
  ROOT, LAD, PROBE_DIR, STUB_ABS, SPACE_LAD, STUB_SPACE_ABS, CODEX,
  stripAnsi, baseEnv, run, codex, procsByPath, tomlLiteral, serverToml, PROVIDER_TOML,
  SPELLINGS, spellingEntries, controlEntry, alternativeEntries, addArgs, readStubLogs, probeEnv,
};
