// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// One line per run: which case, which client, what came of it, and where its files are.
// usage: node index.js > runs-index.json
'use strict';
const fs = require('fs');
const path = require('path');
const R = 'C:/Source/SixFive7/BrowserAI/.work/client-behaviour/runs';
const cases = {
  CCN1: 'control: normal server', CCU0: 'EXCLUDED: stub reused tool_use ids across --continue', CCU1: 'a: tools/list error', CCU2: 'a: tools/list error', CCU3: 'a: tools/list error',
  CCE1: 'a variant: server ends 600 ms after start, inside the retries', CCL1: 'a variant: 90 s idle after the server ended', CCR1: 'a: REAL server 1.1.1-alpha.0.130, stand-in Update.exe',
  CCB1: 'b-shape: real list, calls refused, end at updater exit', CCB2: 'b-shape', CCB3: 'b-shape',
  CCH1: 'hold tools/list until the updater goes, then serve', CCH2: 'hold', CCH3: 'hold',
  CCK1: 'error, then list_changed + serve; tools:{} (BrowserAI\'s capability)', CCK2: 'error then list_changed, tools:{}', CCK3: 'error then list_changed, tools:{}',
  CCKL1: 'error, then list_changed + serve; tools.listChanged declared', CCKL2: 'same, listChanged declared', CCKL3: 'same, listChanged declared',
  CCQN1: 'side: Q261 shape on a re-dialled connection, tools:{}', CCQN2: 'side: Q261, tools:{}', CCQN3: 'side: Q261, tools:{}',
  CCQL1: 'side: Q261 shape on a re-dialled connection, listChanged declared', CCQL2: 'side: Q261, listChanged', CCQL3: 'side: Q261, listChanged',
  CCS1: 'b + keep serving after the updater goes', CCS2: 'b + keep serving', CCS3: 'b + keep serving',
  CXEN1: 'control: normal server', CXEU1: 'a: tools/list error', CXEU2: 'a', CXEU3: 'a', CXER1: 'a: REAL server', CXEB1: 'b-shape', CXEB2: 'b-shape', CXEB3: 'b-shape',
  CXAN1: 'control: normal server', CXAU1: 'a: tools/list error', CXAU2: 'a', CXAU3: 'a', CXAR1: 'a: REAL server', CXAB1: 'b-shape', CXAB2: 'b-shape', CXAB3: 'b-shape',
  CXAH1: 'hold 3 s, default 10 s startup timeout', CXAH2: 'hold', CXAH3: 'hold', CXAS1: 'b + keep serving', CXAS2: 'b + keep serving', CXAS3: 'b + keep serving',
};
const out = [];
for (const name of fs.readdirSync(R).sort()) {
  const dir = path.join(R, name);
  let s = null;
  try { s = JSON.parse(fs.readFileSync(path.join(dir, 'summary.json'), 'utf8')); } catch (e) { /* no summary */ }
  const client = name.startsWith('CC') ? 'claude -p 2.1.282' : name.startsWith('CXE') ? 'codex exec 0.155.0-alpha.9.2' : name.startsWith('CXA') ? 'codex app-server 0.155.0-alpha.9.2' : '?';
  const row = { run: name, client, case: cases[name] || cases[name.replace(/-.*$/, '')] || '(see directory)', dir: dir.replace(/\//g, '\\') };
  if (s) {
    row.serverLaunches = (s.serverLaunches || []).length;
    if (s.toolsListPerLaunch) row.toolsListPerLaunch = s.toolsListPerLaunch;
    if (s.callsPerLaunch) row.callsPerLaunch = s.callsPerLaunch;
    if (s.s1 && s.s1.turns) row.session1ToolsPerTurn = s.s1.turns.map((t) => (t.tools ? t.tools.length : t.toolCount));
    if (s.s2 && s.s2.turns) row.session2ToolsPerTurn = s.s2.turns.map((t) => (t.tools ? t.tools.length : t.toolCount));
    if (s.modelRequests) row.modelToolCountPerRequest = s.modelRequests.map((m) => m.toolCount);
  }
  out.push(row);
}
console.log(JSON.stringify(out, null, 1));
