// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Codex's DEFAULT startup and tool timeouts, read off `codex exec` against a
// stand-in server that answers late. The config.toml sets neither
// startup_timeout_sec nor tool_timeout_sec, which is the whole point.
// Every Codex process gets an allowlisted environment, its own scratch
// CODEX_HOME, a scratch CLAUDE_CONFIG_DIR, and a scripted model on 127.0.0.1;
// it is started with no window and its stdio redirected to files.
//
// usage: node run.js <port> <arm[,arm...]> <ver[,ver...]>
//   arms: S0 (control), S15, S35 (initialize answered after 15 s, 35 s), T75 (a tool call answered after 75 s)
//   ver:  155 (codex-cli 0.155.0-alpha.9.2) | 160 (codex-cli 0.160.0)
'use strict';
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const [port, armArg, verArg] = process.argv.slice(2);
const HERE = __dirname;
const Q = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\q369';
const NODE = 'C:\\Program Files\\nodejs\\node.exe';
const STANDIN = path.join(HERE, 'standin.js');
const MODEL = path.join(Q, 'rig', 'cxmodel.js');
const PROJ = path.join(HERE, 'proj');
const OUT = path.join(HERE, 'runs');
const CODEX = { 155: `${Q}\\bin\\codex155\\codex.exe`, 160: `${Q}\\bin\\codex160\\x86_64-pc-windows-msvc\\bin\\codex.exe` };
const ARMS = {
  S0: { SI_INIT_DELAY_MS: '0', SI_CALL_DELAY_MS: '0' },
  S15: { SI_INIT_DELAY_MS: '15000', SI_CALL_DELAY_MS: '0' },
  S35: { SI_INIT_DELAY_MS: '35000', SI_CALL_DELAY_MS: '0' },
  T75: { SI_INIT_DELAY_MS: '0', SI_CALL_DELAY_MS: '75000' },
};
fs.mkdirSync(OUT, { recursive: true });
fs.mkdirSync(PROJ, { recursive: true });
const POLICY = path.join(OUT, 'policy.json');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const plog = (s) => fs.appendFileSync(path.join(OUT, 'progress.log'), `${new Date().toISOString()} ${s}\n`);

const ALLOW = ['ALLUSERSPROFILE', 'ComSpec', 'CommonProgramFiles', 'CommonProgramFiles(x86)', 'CommonProgramW6432', 'HOMEDRIVE', 'HOMEPATH', 'NUMBER_OF_PROCESSORS', 'OS', 'Path', 'PATHEXT', 'PROCESSOR_ARCHITECTURE', 'PROCESSOR_IDENTIFIER', 'PROCESSOR_LEVEL', 'PROCESSOR_REVISION', 'ProgramData', 'ProgramFiles', 'ProgramFiles(x86)', 'ProgramW6432', 'PUBLIC', 'SystemDrive', 'SystemRoot', 'USERDOMAIN', 'USERNAME', 'USERPROFILE', 'windir'];
function cleanEnv(home) {
  const e = {};
  for (const [k, v] of Object.entries(process.env)) if (ALLOW.some((a) => a.toLowerCase() === k.toLowerCase())) e[k] = v;
  Object.assign(e, {
    CODEX_HOME: home, CLAUDE_CONFIG_DIR: path.join(HERE, 'claude-unused'), DISABLE_AUTOUPDATER: '1', STUB_KEY: 'not-a-real-key',
    RUST_LOG: 'codex_core::mcp=trace,codex_rmcp_client=trace,codex_mcp=trace,info',
    TEMP: path.join(HERE, 'tmp'), TMP: path.join(HERE, 'tmp'), GIT_TERMINAL_PROMPT: '0', GCM_INTERACTIVE: 'never',
    GIT_CEILING_DIRECTORIES: HERE, NO_COLOR: '1',
  });
  return e;
}
const tomlStr = (s) => `'${s}'`;
function configToml(run, serverEnv) {
  const env = Object.assign({ SI_LOG: path.join(run, 'standin.log') }, serverEnv);
  const pairs = Object.entries(env).map(([k, v]) => `${k} = ${tomlStr(v)}`).join(', ');
  return `model = "stub-model"
model_provider = "stub"
approval_policy = "never"
sandbox_mode = "danger-full-access"

[features]
plugins = false

[model_providers.stub]
name = "stub"
base_url = "http://127.0.0.1:${port}"
wire_api = "responses"
env_key = "STUB_KEY"

[mcp_servers.browserai]
command = ${tomlStr(NODE)}
args = [${tomlStr(STANDIN)}]
env = { ${pairs} }

[projects.${tomlStr(PROJ)}]
trust_level = "trusted"

[projects.${tomlStr(PROJ.toLowerCase())}]
trust_level = "trusted"
`;
}

function runProc(exe, args, env, cwd, outFile, errFile, timeoutMs) {
  return new Promise((resolve) => {
    const out = fs.openSync(outFile, 'a');
    const err = fs.openSync(errFile, 'a');
    const started = Date.now();
    const p = spawn(exe, args, { env, cwd, windowsHide: true, stdio: ['ignore', out, err] });
    plog(`SPAWN pid=${p.pid} ${path.basename(exe)} ${JSON.stringify(args).slice(0, 200)}`);
    const t = setTimeout(() => { plog(`TIMEOUT pid=${p.pid}, ended through its own handle`); p.kill(); }, timeoutMs);
    p.on('exit', (code, signal) => { clearTimeout(t); fs.closeSync(out); fs.closeSync(err); plog(`EXIT pid=${p.pid} code=${code} signal=${signal} after ${Date.now() - started} ms`); resolve(code); });
  });
}

(async () => {
  fs.mkdirSync(path.join(HERE, 'tmp'), { recursive: true });
  fs.mkdirSync(path.join(HERE, 'claude-unused'), { recursive: true });
  const stubLog = fs.openSync(path.join(OUT, 'model-stub.out'), 'a');
  const stub = spawn(NODE, [MODEL], { env: Object.assign({}, process.env, { STUB_PORT: port, STUB_POLICY_FILE: POLICY, STUB_LOGDIR: OUT, STUB_TAG: 'model-fallback' }), windowsHide: true, stdio: ['ignore', stubLog, stubLog] });
  plog(`MODEL STUB pid=${stub.pid} port=${port}`);
  await sleep(1500);
  try {
    for (const arm of armArg.split(',')) for (const ver of verArg.split(',')) {
      const name = `${arm}-cx${ver}`;
      const run = path.join(OUT, name);
      fs.mkdirSync(path.join(run, 'home'), { recursive: true });
      fs.writeFileSync(path.join(run, 'home', 'config.toml'), configToml(run, ARMS[arm]));
      fs.writeFileSync(POLICY, JSON.stringify({ logDir: run, tag: 'model', phPerTurn: 0, realTool: 'browserai_list' }));
      plog(`RUN_START ${name}`);
      const args = ['exec', '--json', '--skip-git-repo-check', '--dangerously-bypass-approvals-and-sandbox', '-C', PROJ, 'Use the browserai tools.'];
      await runProc(CODEX[ver], args, cleanEnv(path.join(run, 'home')), PROJ, path.join(run, 'exec.jsonl'), path.join(run, 'exec.err.txt'), 420000);
      plog(`RUN_END ${name}`);
      await sleep(1500);
    }
  } finally {
    stub.kill();
    plog('DONE');
  }
})();
