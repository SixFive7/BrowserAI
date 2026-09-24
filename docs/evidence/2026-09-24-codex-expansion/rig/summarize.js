// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
'use strict';
// Q288 scratch rig: reads every run directory and writes summary.json and
// summary.txt. usage: node summarize.js
const fs = require('fs');
const path = require('path');
const L = require('./lib.js');

const runsDir = path.join(L.ROOT, 'runs');
const rows = [];
const runs = [];
for (const runId of fs.readdirSync(runsDir).sort()) {
  if (!/^[BCD]-/.test(runId) || /-r0$/.test(runId)) continue;
  const dir = path.join(runsDir, runId);
  const resPath = path.join(dir, 'result.json');
  if (!fs.existsSync(resPath)) { runs.push({ runId, missing: true }); continue; }
  const res = JSON.parse(fs.readFileSync(resPath, 'utf8'));
  const setup = JSON.parse(fs.readFileSync(path.join(dir, 'setup.json'), 'utf8'));
  const spec = res.spec;
  const notes = res.notes.filter((n) => n.method === 'mcpServer/startupStatus/updated');
  const listThread = (res.responses.statusListThread && res.responses.statusListThread.result && res.responses.statusListThread.result.data) || [];
  const listNoThread = (res.responses.statusListNoThread && res.responses.statusListNoThread.result && res.responses.statusListNoThread.result.data) || [];
  let mcpList = null;
  try { mcpList = JSON.parse(res.mcpListJson.stdout); } catch (e) { mcpList = null; }
  const alive = res.procs.whileAlive.list || [];
  const launches = res.stubLogs.map((f) => ({ launch: f.events.find((e) => e.event === 'launch'), events: f.events.map((e) => e.event + (e.method ? ':' + e.method : '')) }));
  runs.push({
    runId, layer: spec.layer, spelling: spec.spelling, round: spec.round,
    appServerExit: res.appServerExit, killed: res.killed,
    leftoverAfterExit: (res.procs.afterExit.list || []).length,
    before: (res.procs.before.list || []).length,
    mcpListStatus: res.mcpListJson.status, mcpListStderr: res.mcpListJson.stderr.trim().slice(0, 400),
    threadId: res.threadId,
  });
  for (const e of setup.entries) {
    const name = e.name;
    const failNote = notes.find((n) => n.params.name === name && n.params.status === 'failed');
    const readyNote = notes.find((n) => n.params.name === name && n.params.status === 'ready');
    const lt = listThread.find((d) => d.name === name);
    const ln = listNoThread.find((d) => d.name === name);
    const ml = mcpList ? mcpList.find((d) => d.name === name) : null;
    const tc = res.toolCalls[name];
    let tcLaunch = null;
    if (tc && tc.result && tc.result.content && tc.result.content[0]) { try { tcLaunch = JSON.parse(tc.result.content[0].text); } catch (err) { tcLaunch = { raw: tc.result.content[0].text }; } }
    const aliveHere = alive.filter((p) => (p.commandLine || '').includes(`--tag ${name}`) && !(p.commandLine || '').includes(`--tag ${name}_`));
    const tagged = launches.filter((l) => l.launch && l.launch.env && l.launch.env.PROBE_TAG === name);
    const add = (setup.adds || []).find((a) => a.name === name);
    const get = (setup.gets || []).find((g) => g.name === name);
    let getJson = null;
    if (get) { try { getJson = JSON.parse(get.result.stdout); } catch (err) { getJson = null; } }
    rows.push({
      runId, layer: spec.layer, spelling: spec.spelling, round: spec.round, name,
      configured: { command: e.command, args: e.args, spelledEnv: e.env && e.env.PROBE_SPELLED, envPath: e.env && e.env.PATH, envLocalAppData: e.env && e.env.LOCALAPPDATA },
      startup: res.startupStatus[name] || null,
      startupError: failNote ? failNote.params.error : null,
      readyAt: readyNote ? readyNote.at : null,
      statusListThread: lt ? { runtimeStatus: lt.runtimeStatus, serverInfo: lt.serverInfo ? `${lt.serverInfo.name} ${lt.serverInfo.version}` : null, tools: Object.keys(lt.tools || {}), toolsError: lt.toolsError } : null,
      statusListNoThread: ln ? { runtimeStatus: ln.runtimeStatus, serverInfo: ln.serverInfo ? `${ln.serverInfo.name} ${ln.serverInfo.version}` : null, tools: Object.keys(ln.tools || {}), toolsError: ln.toolsError } : null,
      aliveByPath: aliveHere.map((p) => ({ pid: p.pid, path: p.path, parentPath: p.parentPath, commandLine: p.commandLine })),
      toolCallLaunch: tcLaunch ? { pid: tcLaunch.pid, image: tcLaunch.image, argv: tcLaunch.argv, envSpelled: tcLaunch.env && tcLaunch.env.PROBE_SPELLED, envPath: tcLaunch.env && tcLaunch.env.PATH && tcLaunch.env.PATH.length < 200 ? tcLaunch.env.PATH : (tcLaunch.env && tcLaunch.env.PATH ? '(long)' : null), localAppData: tcLaunch.env && tcLaunch.env.LOCALAPPDATA } : null,
      stubLaunchesTagged: tagged.length,
      stubAnsweredInitialize: tagged.some((l) => l.events.includes('sent:initialize')),
      mcpList: ml ? { enabled: ml.enabled, disabled_reason: ml.disabled_reason, command: ml.transport.command, args: ml.transport.args, spelledEnv: ml.transport.env && ml.transport.env.PROBE_SPELLED } : null,
      mcpAdd: add ? { status: add.result.status, stdout: add.result.stdout.trim(), stderr: add.result.stderr.trim().slice(0, 300) } : null,
      mcpGet: getJson ? { command: getJson.transport && getJson.transport.command, args: getJson.transport && getJson.transport.args, spelledEnv: getJson.transport && getJson.transport.env && getJson.transport.env.PROBE_SPELLED } : (get ? { status: get.result.status, stderr: get.result.stderr.trim().slice(0, 300) } : null),
    });
  }
}

// Aggregate: one line per (layer, entry name).
const groups = new Map();
for (const r of rows) {
  const k = `${r.layer} | ${r.name}`;
  if (!groups.has(k)) groups.set(k, []);
  groups.get(k).push(r);
}
const lines = [];
for (const [k, rs] of [...groups.entries()].sort()) {
  const statuses = rs.map((r) => r.startup).join(',');
  const errs = [...new Set(rs.map((r) => r.startupError).filter(Boolean))];
  const alive = rs.map((r) => r.aliveByPath.length).join(',');
  const init = rs.map((r) => (r.stubAnsweredInitialize ? 'y' : 'n')).join(',');
  const argv0 = [...new Set(rs.map((r) => (r.toolCallLaunch && r.toolCallLaunch.argv ? JSON.stringify(r.toolCallLaunch.argv) : null)).filter(Boolean))];
  const images = [...new Set(rs.map((r) => (r.toolCallLaunch ? r.toolCallLaunch.image : null)).filter(Boolean))];
  const parents = [...new Set(rs.flatMap((r) => r.aliveByPath.map((p) => p.parentPath)))];
  const listCmd = [...new Set(rs.map((r) => (r.mcpList ? r.mcpList.command : '(absent)')))];
  const lt = [...new Set(rs.map((r) => (r.statusListThread ? `${r.statusListThread.runtimeStatus}/${r.statusListThread.serverInfo ? 'serverInfo' : 'noInfo'}/${r.statusListThread.toolsError ? 'toolsError' : 'tools:' + r.statusListThread.tools.join('+')}` : '(absent)')))];
  const adds = [...new Set(rs.map((r) => (r.mcpAdd ? `exit ${r.mcpAdd.status}` : '-')))];
  const getCmd = [...new Set(rs.map((r) => (r.mcpGet && r.mcpGet.command !== undefined ? r.mcpGet.command : '-')))];
  lines.push(`${k}\n  rounds=${rs.length} startup=[${statuses}] aliveByPath=[${alive}] answeredInitialize=[${init}]\n  error=${JSON.stringify(errs)}\n  statusList(thread)=${JSON.stringify(lt)}\n  image=${JSON.stringify(images)} parent=${JSON.stringify(parents)}\n  argvSeen=${JSON.stringify(argv0)}\n  mcpList.command=${JSON.stringify(listCmd)}\n  mcpAdd=${JSON.stringify(adds)} mcpGet.command=${JSON.stringify(getCmd)}`);
}
const runLines = runs.map((r) => JSON.stringify(r));
fs.writeFileSync(path.join(L.ROOT, 'summary.json'), JSON.stringify({ runs, rows }, null, 1));
fs.writeFileSync(path.join(L.ROOT, 'summary.txt'), ['RUNS', ...runLines, '', 'CASES', ...lines, ''].join('\n'));
console.log(`runs=${runs.length} rows=${rows.length} groups=${groups.size}`);
