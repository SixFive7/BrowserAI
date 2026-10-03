// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Codex's DEFAULT MCP startup timeout, read off `codex app-server`: a thread is
// started against the late-answering stand-in, and the driver waits for the
// server's terminal mcpServer/startupStatus/updated, which carries ready or
// failed and its time. `codex exec` cannot show this: it starts its turn about a
// second after launch whether the server is ready or not.
//
// usage: node run-app.js <arm[,arm...]> <ver[,ver...]>
//   arms: A0 (control), A15, A35 (initialize answered after 15 s, 35 s)
'use strict';
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const [armArg, verArg] = process.argv.slice(2);
const HERE = __dirname;
const Q = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\q369';
const NODE = 'C:\\Program Files\\nodejs\\node.exe';
const STANDIN = path.join(HERE, 'standin.js');
const DRIVER = path.join(Q, 'rig', 'appdrv.js');
const PROJ = path.join(HERE, 'proj');
const OUT = path.join(HERE, 'runs-app');
const CODEX = { 155: `${Q}\\bin\\codex155\\codex.exe`, 160: `${Q}\\bin\\codex160\\x86_64-pc-windows-msvc\\bin\\codex.exe` };
const ARMS = { A0: '0', A15: '15000', A35: '35000' };
fs.mkdirSync(OUT, { recursive: true });
const plog = (s) => fs.appendFileSync(path.join(OUT, 'progress.log'), `${new Date().toISOString()} ${s}\n`);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

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
const configToml = (run, initDelay) => `model = "stub-model"
model_provider = "stub"
approval_policy = "never"
sandbox_mode = "danger-full-access"

[features]
plugins = false

[model_providers.stub]
name = "stub"
base_url = "http://127.0.0.1:9"
wire_api = "responses"
env_key = "STUB_KEY"

[mcp_servers.browserai]
command = ${tomlStr(NODE)}
args = [${tomlStr(STANDIN)}]
env = { SI_LOG = ${tomlStr(path.join(run, 'standin.log'))}, SI_INIT_DELAY_MS = '${initDelay}', SI_CALL_DELAY_MS = '0' }

[projects.${tomlStr(PROJ)}]
trust_level = "trusted"

[projects.${tomlStr(PROJ.toLowerCase())}]
trust_level = "trusted"
`;

function runProc(exe, args, env, cwd, outFile, errFile, timeoutMs) {
  return new Promise((resolve) => {
    const out = fs.openSync(outFile, 'a');
    const err = fs.openSync(errFile, 'a');
    const p = spawn(exe, args, { env, cwd, windowsHide: true, stdio: ['ignore', out, err] });
    plog(`SPAWN pid=${p.pid} ${path.basename(exe)} ${JSON.stringify(args).slice(0, 200)}`);
    const t = setTimeout(() => { plog(`TIMEOUT pid=${p.pid}, ended through its own handle`); p.kill(); }, timeoutMs);
    p.on('exit', (code, signal) => { clearTimeout(t); fs.closeSync(out); fs.closeSync(err); plog(`EXIT pid=${p.pid} code=${code} signal=${signal}`); resolve(code); });
  });
}

(async () => {
  for (const arm of armArg.split(',')) for (const ver of verArg.split(',')) {
    const name = `${arm}-cx${ver}`;
    const run = path.join(OUT, name);
    fs.rmSync(run, { recursive: true, force: true });
    fs.mkdirSync(path.join(run, 'home'), { recursive: true });
    fs.writeFileSync(path.join(run, 'home', 'config.toml'), configToml(run, ARMS[arm]));
    const steps = [
      { m: 'initialize', p: { clientInfo: { name: 'timeout-probe', title: 'timeout probe', version: '1.0.0' }, capabilities: { experimentalApi: true } } },
      { m: 'thread/start', p: { cwd: PROJ } },
      { waitStatus: 'browserai', timeout: 90000 },
    ];
    fs.writeFileSync(path.join(run, 'steps.json'), JSON.stringify(steps, null, 1));
    const appEnv = cleanEnv(path.join(run, 'home'));
    fs.writeFileSync(path.join(run, 'appenv.json'), JSON.stringify(appEnv));
    const denv = Object.assign({}, appEnv, { CODEX_EXE: CODEX[ver], DRIVER_LOG: path.join(run, 'driver.log'), DRIVER_CWD: PROJ, APP_ENV_FILE: path.join(run, 'appenv.json'), DRIVER_KILL_AFTER_MS: '10000' });
    plog(`RUN_START ${name}`);
    await runProc(NODE, [DRIVER, path.join(run, 'steps.json')], denv, PROJ, path.join(run, 'driver.out'), path.join(run, 'driver.err'), 200000);
    plog(`RUN_END ${name}`);
    await sleep(1500);
  }
  plog('DONE');
})();
