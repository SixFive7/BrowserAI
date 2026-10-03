// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// One `codex app-server` run, summarised from its own files.
// usage: node app-summary.js <run-dir> <run-name>
'use strict';
const fs = require('fs');
const path = require('path');
const [dir, name] = process.argv.slice(2);
const rd = (p) => { try { return fs.readFileSync(path.join(dir, p), 'utf8'); } catch (e) { return null; } };
const out = { run: name };
const launches = (rd(`logs/${name}.launches.log`) || '').split('\n').filter(Boolean);
out.serverLaunches = launches.filter((l) => / LAUNCH /.test(l)).map((l) => l.replace(/ argv=.*$/, '').replace(/ BROWSERAI_ROOT=.*$/, ''));
out.serverEvents = launches.filter((l) => !/ initialize from /.test(l)).map((l) => l.slice(0, 200));
const drv = (rd(`logs/${name}.driver.log`) || '').split('\n').filter(Boolean);
out.steps = [];
let cur = null;
for (const l of drv) {
  const mk = l.match(/ MARKER (.*)$/);
  if (mk) { cur = { marker: mk[1], events: [] }; out.steps.push(cur); continue; }
  const push = (s) => { if (!cur) { cur = { marker: '(start)', events: [] }; out.steps.push(cur); } cur.events.push(s); };
  if (/ NOTE#\d+ mcpServer\/startupStatus\/updated /.test(l)) { const j = JSON.parse(l.replace(/^.*? mcpServer\/startupStatus\/updated /, '')); push(`startupStatus ${j.name} ${j.status}${j.error ? ' error=' + JSON.stringify(j.error) : ''}`); }
  else if (/ RESP id=\d+ \{"(code|content)"/.test(l)) push(`RESP ${l.replace(/^.*? RESP id=\d+ /, '')}`);
  else if (/item\/completed \{"item":\{"type":"mcpToolCall"/.test(l)) { const j = JSON.parse(l.replace(/^.*? item\/completed /, '')); push(`model-turn mcpToolCall status=${j.item.status} error=${JSON.stringify(j.item.error)} result=${JSON.stringify(j.item.result && j.item.result.content)}`); }
  else if (/ LAUNCH-LIVENESS /.test(l)) push(l.replace(/^.*? LAUNCH-LIVENESS /, 'liveness '));
  else if (/ (REMOVED|TOUCHED|WAITFILE|WAIT TIMEOUT|TIMEOUT id) /.test(l)) push(l.replace(/^\S+ /, ''));
}
const reqs = (rd(`logs/${name}-model.requests.jsonl`) || '').split('\n').filter(Boolean).map((l) => JSON.parse(l));
out.modelRequests = reqs.map((d) => {
  const b = d.body; const s = JSON.stringify(b);
  const ns = (b.tools || []).filter((t) => t.type === 'namespace' && /browserai/i.test(t.name));
  const outputs = (b.input || []).filter((i) => i.type === 'function_call_output');
  return { turn: d.turn, toolCount: (b.tools || []).length, browseraiNamespace: ns.map((t) => ({ description: t.description, tools: (t.tools || []).map((x) => x.name) })), updateSentenceOccurrences: (s.match(/installing an update/g) || []).length, lastOutput: outputs.length ? outputs[outputs.length - 1].output : null };
});
console.log(JSON.stringify(out, null, 1));
