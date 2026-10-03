// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Q369 batch generator for the ExitRig harness (Claude Code runs).
// usage: node gen.js <batch> <scenario[,scenario...]> <client[,client...]> <reps> [port]
//   clients: cli (2.1.288) | vsc287 (the VS Code extension's 2.1.287 binary)
//   scenarios: see SCEN below
'use strict';
const fs = require('fs');
const path = require('path').win32;

const [batch, scenArg, clientArg, repsArg, portArg] = process.argv.slice(2);
const S = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\q369';
const RIG = `${S}\\rig\\bin\\ExitRig.exe`;
const NODE = 'C:\\Program Files\\nodejs\\node.exe';
const PH = `${S}\\rig\\phserver.js`;
const PROJ = `${S}\\proj`;
const PORT = portArg || '8961';
const BIN = { cli: `${S}\\bin\\cli\\claude.exe`, vsc287: `${S}\\bin\\vscode287\\claude.exe` };
const SDKVER = { cli: '0.3.288', vsc287: '0.3.287' };
const BATCHDIR = `${S}\\runs\\${batch}`;
const POLICY = `${BATCHDIR}\\policy.json`;
fs.mkdirSync(BATCHDIR, { recursive: true });
fs.mkdirSync(PROJ, { recursive: true });

const baseEnv = (run, extra) => Object.assign({
  CLAUDE_CONFIG_DIR: `${run}\\cfg`, CODEX_HOME: `${S}\\cfg\\codex-unused`, DISABLE_AUTOUPDATER: '1',
  ANTHROPIC_BASE_URL: `http://127.0.0.1:${PORT}`, ANTHROPIC_API_KEY: 'stub-key-not-real',
  CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: '1', DISABLE_TELEMETRY: '1', DISABLE_ERROR_REPORTING: '1',
  GIT_TERMINAL_PROMPT: '0', GCM_INTERACTIVE: 'never', DOTNET_CLI_TELEMETRY_OPTOUT: '1',
  TEMP: `${S}\\tmp`, TMP: `${S}\\tmp`, APPDATA: `${S}\\appdata\\roaming`, LOCALAPPDATA: `${S}\\appdata\\local`,
  GIT_CEILING_DIRECTORIES: S,
}, extra || {});

// The VS Code extension's own environment for its claude process (extension.js 2.1.288, U$()):
// MCP_CONNECTION_NONBLOCKING, CLAUDE_CODE_ENABLE_TASKS, CLAUDE_CODE_EMIT_STARTUP_TIMING, ENTRYPOINT.
const vscEnv = (client) => ({
  CLAUDE_CODE_ENTRYPOINT: 'claude-vscode', CLAUDE_AGENT_SDK_VERSION: SDKVER[client],
  MCP_CONNECTION_NONBLOCKING: 'true', CLAUDE_CODE_ENABLE_TASKS: '0', CLAUDE_CODE_EMIT_STARTUP_TIMING: '1',
});

const seed = (lastSeen) => JSON.stringify({
  hasCompletedOnboarding: true, autoUpdates: false, bypassPermissionsModeAccepted: false,
  customApiKeyResponses: { approved: ['stub-key-not-real'], rejected: [] },
  lastReleaseNotesSeen: lastSeen,
  projects: { [PROJ.replace(/\\/g, '/')]: { hasTrustDialogAccepted: true, hasCompletedProjectOnboarding: true, allowedTools: [] } },
});

const mcpJson = (run, serverEnv) => JSON.stringify({ mcpServers: { browserai: { type: 'stdio', command: NODE, args: [PH], env: Object.assign({ PH_LOGDIR: run, PH_TAG: 'ph' }, serverEnv) } } });

const ALLOWED = 'mcp__browserai__update_in_flight,mcp__browserai__browserai_init,mcp__browserai__browserai_list';
const userMsg = (text) => JSON.stringify({ type: 'user', session_id: '', message: { role: 'user', content: [{ type: 'text', text }] }, parent_tool_use_id: null });
let reqNo = 1;
const ctl = (request) => JSON.stringify({ type: 'control_request', request_id: `req_${reqNo++}`, request });
const initReq = () => ctl({ subtype: 'initialize' });
const turn = (text, timeoutMs) => [{ do: 'send', line: userMsg(text) }, { do: 'waitFor', pattern: '"type":"result"', timeoutMs: timeoutMs || 90000 }];
const status = (label) => { const l = ctl({ subtype: 'mcp_status' }); const id = JSON.parse(l).request_id; return [{ do: 'mark', text: `mcp_status ${label}` }, { do: 'send', line: l }, { do: 'waitFor', pattern: `"request_id":"${id}"`, timeoutMs: 30000 }]; };
const control = (label, request) => { const l = ctl(request); const id = JSON.parse(l).request_id; return [{ do: 'mark', text: label }, { do: 'send', line: l }, { do: 'waitFor', pattern: `"request_id":"${id}"`, timeoutMs: 60000 }]; };

// ---- scenarios ---------------------------------------------------------------------------
// Each returns { mode: P|S, server: {...env}, policy: {...}, prompt (P), steps (S) }
const SCEN = {
  // P mode (claude -p): one turn. The update ends 1 s after the first placeholder call, inside the turn.
  PA: (run) => ({ mode: 'P', server: { PH_CAP: 'listChanged', PH_MODES: 'placeholder', PH_DONE_AFTER_CALL_MS: '1000', PH_NOTIFY_ON_DONE: '1' }, policy: { phPerTurn: 2, delayAfterPhMs: 4000 } }),
  PA0: (run) => ({ mode: 'P', server: { PH_CAP: 'none', PH_MODES: 'placeholder', PH_DONE_AFTER_CALL_MS: '1000', PH_NOTIFY_ON_DONE: '1' }, policy: { phPerTurn: 2, delayAfterPhMs: 4000 } }),
  // P mode, arm b: launch 1 (placeholder) ends itself 300 ms after its first call; the second call
  // re-dials launch 2 (real), which sends list_changed right after notifications/initialized.
  PB: (run) => ({ mode: 'P', server: { PH_CAP: 'listChanged', PH_MODES: 'placeholder,real', PH_KILL_AFTER_CALL: '1', PH_NOTIFY_AFTER_INIT: '2,3,4' }, policy: { phPerTurn: 2, delayAfterPhMs: 4000 } }),
  // ... the same, with the relaunched server's list_changed 1.5 s after notifications/initialized.
  PBd: (run) => ({ mode: 'P', server: { PH_CAP: 'listChanged', PH_MODES: 'placeholder,real', PH_KILL_AFTER_CALL: '1', PH_NOTIFY_AFTER_INIT: '2,3,4', PH_NOTIFY_AFTER_INIT_DELAY_MS: '1500' }, policy: { phPerTurn: 2, delayAfterPhMs: 4000 } }),

  // S mode (stream-json, the VS Code extension's transport). Arm a: the update ends between turns.
  SA: (run) => ({ mode: 'S', server: { PH_CAP: 'listChanged', PH_MODES: 'placeholder', PH_DONE_FILE: `${run}\\update.done`, PH_NOTIFY_ON_DONE: '1' }, policy: { phPerTurn: 1 },
    steps: [ ...status('at start'), ...turn('Use the browserai tools.'),
      { do: 'mark', text: 'UPDATE ENDS' }, { do: 'touch', path: `${run}\\update.done` }, { do: 'sleep', ms: 3000 },
      ...status('after the update ended'), ...turn('Try the browserai tools again.'), { do: 'sleep', ms: 1000 } ] }),
  SA0: (run) => { const s = SCEN.SA(run); s.server.PH_CAP = 'none'; return s; },
  // Arm b: launch 1 ends itself after its first call; 10 s idle (does anything re-dial on its own?);
  // turn 2 re-dials launch 2, which sends list_changed right after notifications/initialized; turn 3.
  SB: (run) => ({ mode: 'S', server: { PH_CAP: 'listChanged', PH_MODES: 'placeholder,real', PH_KILL_AFTER_CALL: '1', PH_NOTIFY_AFTER_INIT: '2,3,4' }, policy: { phPerTurn: 1 },
    steps: [ ...turn('Use the browserai tools.'), { do: 'mark', text: 'IDLE 10 s after launch 1 ended' }, { do: 'sleep', ms: 10000 },
      ...status('after launch 1 ended'), ...turn('Try the browserai tools again.'), { do: 'sleep', ms: 4000 },
      ...status('after the re-dial'), ...turn('And once more.'), { do: 'sleep', ms: 1000 } ] }),
  // Arm c: the update ends but the server never notifies (so the client still holds the placeholder);
  // turn 2 meets the stale text; then the host sends mcp_reconnect (VS Code's MCP dialog "Reconnect").
  SC: (run) => ({ mode: 'S', server: { PH_CAP: 'listChanged', PH_MODES: 'placeholder', PH_DONE_FILE: `${run}\\update.done`, PH_NOTIFY_ON_DONE: '0' }, policy: { phPerTurn: 1 },
    steps: [ ...turn('Use the browserai tools.'), { do: 'mark', text: 'UPDATE ENDS (no notification)' }, { do: 'touch', path: `${run}\\update.done` }, { do: 'sleep', ms: 2000 },
      ...turn('Try the browserai tools again.'), ...status('before the reconnect'),
      ...control('mcp_reconnect browserai', { subtype: 'mcp_reconnect', serverName: 'browserai' }), { do: 'sleep', ms: 3000 },
      ...status('after the reconnect'), ...turn('And once more.'), { do: 'sleep', ms: 1000 } ] }),
  // Arm c': the same with the host's disable and enable (VS Code's MCP dialog "Disable", then enable).
  SCt: (run) => ({ mode: 'S', server: { PH_CAP: 'listChanged', PH_MODES: 'placeholder', PH_DONE_FILE: `${run}\\update.done`, PH_NOTIFY_ON_DONE: '0' }, policy: { phPerTurn: 1 },
    steps: [ ...turn('Use the browserai tools.'), { do: 'mark', text: 'UPDATE ENDS (no notification)' }, { do: 'touch', path: `${run}\\update.done` }, { do: 'sleep', ms: 2000 },
      ...control('mcp_toggle off', { subtype: 'mcp_toggle', serverName: 'browserai', enabled: false }), { do: 'sleep', ms: 2000 },
      ...control('mcp_toggle on', { subtype: 'mcp_toggle', serverName: 'browserai', enabled: true }), { do: 'sleep', ms: 3000 },
      ...status('after the toggle'), ...turn('Try the browserai tools again.'), { do: 'sleep', ms: 1000 } ] }),
  // Arm c on a DEAD server: launch 1 ends itself, nothing is called, the host reconnects.
  SCd: (run) => ({ mode: 'S', server: { PH_CAP: 'listChanged', PH_MODES: 'placeholder,real', PH_KILL_AFTER_CALL: '1' }, policy: { phPerTurn: 1 },
    steps: [ ...turn('Use the browserai tools.'), { do: 'sleep', ms: 2000 }, ...status('after launch 1 ended'),
      ...control('mcp_reconnect browserai', { subtype: 'mcp_reconnect', serverName: 'browserai' }), { do: 'sleep', ms: 3000 },
      ...status('after the reconnect'), ...turn('Try the browserai tools again.'), { do: 'sleep', ms: 1000 } ] }),
  // Model-side refresh (Claude Code's own RefreshMcpTools tool, offered when the default tools are on).
  // SR: live server; the update ends with no notification and no listChanged capability.
  SR: (run) => ({ mode: 'S', defaultTools: true, server: { PH_CAP: 'none', PH_MODES: 'placeholder', PH_DONE_FILE: `${run}\\update.done`, PH_NOTIFY_ON_DONE: '0' }, policy: { phPerTurn: 1, refreshTool: 'RefreshMcpTools', refreshInput: { server: 'browserai' } },
    steps: [ ...turn('Use the browserai tools.'), { do: 'mark', text: 'UPDATE ENDS (no notification, no capability)' }, { do: 'touch', path: `${run}\\update.done` }, { do: 'sleep', ms: 2000 },
      ...turn('Try the browserai tools again.'), ...status('after turn 2'), { do: 'sleep', ms: 1000 } ] }),
  // SRb: launch 1 ends itself after its first call; turn 2 re-dials launch 2 (real), meets the stale
  // text, and the model calls RefreshMcpTools; turn 3.
  SRb: (run) => ({ mode: 'S', defaultTools: true, server: { PH_CAP: 'listChanged', PH_MODES: 'placeholder,real', PH_KILL_AFTER_CALL: '1' }, policy: { phPerTurn: 1, refreshTool: 'RefreshMcpTools', refreshInput: { server: 'browserai' } },
    steps: [ ...turn('Use the browserai tools.'), { do: 'sleep', ms: 2000 },
      ...turn('Try the browserai tools again.'), ...status('after turn 2'), ...turn('And once more.'), { do: 'sleep', ms: 1000 } ] }),
  // SAts: SA with tool search forced on (ENABLE_TOOL_SEARCH=true, the first-party default) and the
  // default tools: what the model is shown for a deferred placeholder, and after the refresh.
  SAts: (run) => Object.assign(SCEN.SA(run), { defaultTools: true, extraEnv: { ENABLE_TOOL_SEARCH: 'true' }, policy: { phPerTurn: 1, refreshTool: 'RefreshMcpTools', refreshInput: { server: 'browserai' } } }),
  // Controls for the directions: the REAL list at first connect, the server killed after its first
  // call (as an apply's kill pass ends a server that started before the swap), then the re-dial.
  PBr: (run) => { const d = SCEN.PB(run); d.server.PH_MODES = 'real,real'; d.policy = { phPerTurn: 1, realPerTurn: 2, delayAfterRealMs: 1500 }; return d; },
  SBr: (run) => { const d = SCEN.SB(run); d.server.PH_MODES = 'real,real'; return d; },
  // Q369's hazard itself: launch 1 is killed on receiving initialize, before the handshake finished.
  PK: (run) => ({ mode: 'P', server: { PH_CAP: 'listChanged', PH_MODES: 'real,real', PH_DIE_ON: 'initialize', PH_KILL_LAUNCHES: '1' }, policy: { phPerTurn: 1 } }),
  SK: (run) => ({ mode: 'S', server: { PH_CAP: 'listChanged', PH_MODES: 'real,real', PH_DIE_ON: 'initialize', PH_KILL_LAUNCHES: '1' }, policy: { phPerTurn: 1 },
    steps: [ { do: 'sleep', ms: 3000 }, ...status('after launch 1 died mid-handshake'), ...turn('Use the browserai tools.'), { do: 'sleep', ms: 2000 }, ...status('after turn 1'),
      ...control('mcp_reconnect browserai', { subtype: 'mcp_reconnect', serverName: 'browserai' }), { do: 'sleep', ms: 3000 },
      ...status('after the reconnect'), ...turn('Try the browserai tools again.'), { do: 'sleep', ms: 1000 } ] }),
  SBts: (run) => Object.assign(SCEN.SB(run), { defaultTools: true, extraEnv: { ENABLE_TOOL_SEARCH: 'true' }, policy: { phPerTurn: 1, refreshTool: 'RefreshMcpTools', refreshInput: { server: 'browserai' } } }),
  // PR: claude -p, one turn: the placeholder call, the update ends 500 ms later (no notification,
  // no capability), the model calls RefreshMcpTools, then the real tool.
  PR: (run) => ({ mode: 'P', defaultTools: true, server: { PH_CAP: 'none', PH_MODES: 'placeholder', PH_DONE_AFTER_CALL_MS: '500', PH_NOTIFY_ON_DONE: '0' }, policy: { phPerTurn: 1, delayAfterPhMs: 2000, refreshTool: 'RefreshMcpTools', refreshInput: { server: 'browserai' } } }),
};

const clients = clientArg.split(',');
const scens = scenArg.split(',');
const reps = Number(repsArg || 3);
const runs = [];
for (const sc of scens) for (const client of clients) for (let r = 1; r <= reps; r++) {
  const name = `${sc}-${client}-r${r}`;
  const run = `${BATCHDIR}\\${name}`;
  const def = SCEN[sc](run);
  // With defaultTools the built-in tools stay on (RefreshMcpTools is one of them); otherwise none.
  const toolArgs = def.defaultTools ? ['--allowedTools', `${ALLOWED},RefreshMcpTools,WaitForMcpServers`] : ['--tools', '', '--allowedTools', ALLOWED];
  const args = def.mode === 'P'
    ? ['-p', '--output-format', 'stream-json', '--verbose', '--mcp-config', `${run}\\mcp.json`, '--strict-mcp-config', ...toolArgs, '--permission-mode', 'acceptEdits', '--debug-file', `${run}\\claude-debug.log`, '--model', 'sonnet', 'Use the browserai tools.']
    : ['--output-format', 'stream-json', '--verbose', '--input-format', 'stream-json', '--mcp-config', `${run}\\mcp.json`, '--strict-mcp-config', ...toolArgs, '--permission-mode', 'acceptEdits', '--debug-file', `${run}\\claude-debug.log`, '--model', 'sonnet'];
  const env = baseEnv(run, Object.assign(def.mode === 'S' ? vscEnv(client) : {}, def.extraEnv || {}));
  const policy = Object.assign({ logDir: run, tag: 'model' }, def.policy);
  const steps = def.mode === 'P' ? [] : [{ do: 'send', line: initReq() }, { do: 'waitFor', pattern: '"type":"control_response"', timeoutMs: 30000 }, ...def.steps, { do: 'mark', text: 'end: closeStdin' }, { do: 'closeStdin' }];
  runs.push({
    name, dir: run, exe: BIN[client], args, cwd: PROJ, env, stdin: def.mode === 'P' ? 'closed' : 'pipe', maxMs: 240000, settleMs: 20000,
    writeFiles: [
      { path: `${run}\\cfg\\.claude.json`, content: seed(client === 'cli' ? '2.1.288' : '2.1.287') },
      { path: `${run}\\mcp.json`, content: mcpJson(run, def.server) },
      { path: POLICY, content: JSON.stringify(policy) },
    ],
    steps,
  });
}
const batchObj = {
  progress: `${BATCHDIR}\\progress.log`, gapMs: 1500,
  stubs: [{ name: 'anthropic', exe: NODE, args: [`${S}\\rig\\ccmodel.js`], env: { STUB_PORT: PORT, STUB_POLICY_FILE: POLICY, STUB_LOGDIR: BATCHDIR, STUB_TAG: 'stub-fallback' } }],
  runs,
};
fs.writeFileSync(`${BATCHDIR}\\batch.json`, JSON.stringify(batchObj, null, 1));
console.log(`${batch}: ${runs.length} runs -> ${BATCHDIR}\\batch.json`);
