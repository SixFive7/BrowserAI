// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
'use strict';
// Q288 scratch rig: confirms that nothing this rig started is still running.
// Stubs are looked for by PATH; app-servers by the pid each run recorded,
// checked against the Codex image path so a recycled pid is not mistaken.
const fs = require('fs');
const path = require('path');
const L = require('./lib.js');

const runsDir = path.join(L.ROOT, 'runs');
const pids = [];
for (const runId of fs.readdirSync(runsDir)) {
  const p = path.join(runsDir, runId, 'result.json');
  if (!fs.existsSync(p)) continue;
  const r = JSON.parse(fs.readFileSync(p, 'utf8'));
  if (r.appServerPid) pids.push({ runId, pid: r.appServerPid });
}
const stubs = L.procsByPath([L.STUB_ABS, L.STUB_SPACE_ABS]);
const filter = pids.map((x) => `ProcessId = ${x.pid}`).join(' OR ');
const script = `$ErrorActionPreference='Stop'; ConvertTo-Json -Compress -InputObject @(Get-CimInstance Win32_Process -Filter "${filter}" | Select-Object ProcessId,ExecutablePath)`;
const q = L.run('pwsh.exe', ['-NoProfile', '-NonInteractive', '-Command', script]);
let alive = [];
try { alive = q.stdout.trim() ? JSON.parse(q.stdout) : []; } catch (e) { alive = [{ parseError: q.stdout }]; }
const codexStill = alive.filter((p) => p.ExecutablePath && p.ExecutablePath.toLowerCase() === L.CODEX.toLowerCase());
console.log(JSON.stringify({ at: new Date().toISOString(), appServerPidsRecorded: pids.length, pidsStillPresent: alive, codexAppServersStillRunning: codexStill, stubsByPath: stubs.list, queryStatus: [stubs.status, q.status] }, null, 1));
