// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// One Claude Code run, summarised from its own files.
// usage: node cc-summary.js <run-dir> <run-name>
'use strict';
const fs = require('fs');
const path = require('path');
const [dir, name] = process.argv.slice(2);
const rd = (p) => { try { return fs.readFileSync(path.join(dir, p), 'utf8'); } catch (e) { return null; } };
const out = { run: name };
const launches = (rd(`logs/${name}.launches.log`) || '').split('\n').filter(Boolean);
out.serverLaunches = launches.filter((l) => / LAUNCH /.test(l)).map((l) => l.replace(/ argv=.*$/, ''));
out.toolsListPerLaunch = {};
for (const l of launches) { const m = l.match(/launch=(\d+) .*tools\/list (REFUSED|answered)/); if (m) out.toolsListPerLaunch[m[1]] = (out.toolsListPerLaunch[m[1]] || []).concat(m[2]); }
out.callsPerLaunch = {};
for (const l of launches) { const m = l.match(/launch=(\d+) .*tools\/call (\S+) (\S+)/); if (m) out.callsPerLaunch[m[1]] = (out.callsPerLaunch[m[1]] || []).concat(`${m[2]} ${m[3]}`); }
out.ending = launches.filter((l) => /ENDING|exited code|client closed/.test(l)).map((l) => l.slice(0, 160));
for (const s of [1, 2]) {
  const stream = rd(`out/s${s}.stream.jsonl`);
  if (!stream) continue;
  const lines = stream.split('\n').filter(Boolean).map((l) => JSON.parse(l));
  const init = lines.find((j) => j.type === 'system' && j.subtype === 'init');
  const res = lines.find((j) => j.type === 'result');
  out[`s${s}`] = {
    init: init ? { mcp_servers: init.mcp_servers, tools: init.tools } : null,
    result: res ? { subtype: res.subtype, is_error: res.is_error, result: res.result } : null,
    toolResults: lines.filter((j) => j.type === 'user').flatMap((j) => (j.message.content || []).filter((c) => c.type === 'tool_result').map((c) => c.content)),
  };
  const dbg = (rd(`out/s${s}.debug.log`) || '').split('\n').filter((l) => /MCP server "browserai"|No such tool/.test(l));
  out[`s${s}`].debugMcpLines = dbg.map((l) => l.slice(0, 700));
  const reqs = (rd(`logs/${name}-s${s}.requests.jsonl`) || '').split('\n').filter(Boolean).map((l) => JSON.parse(l));
  out[`s${s}`].turns = reqs.map((d) => {
    const b = d.body; const str = JSON.stringify(b);
    const msgs = b.messages || [];
    const last = msgs.filter((m) => m.role === 'user' && Array.isArray(m.content) && m.content.some((c) => c.type === 'tool_result')).pop();
    return {
      turn: d.turn,
      tools: (b.tools || []).map((t) => t.name),
      instructionsSeen: str.match(/UPDSTUB-INSTRUCTIONS launch=\d+ behaves=\w+/g) || [],
      updateSentenceOccurrences: (str.match(/installing an update/g) || []).length,
      lastToolResult: last ? last.content.filter((c) => c.type === 'tool_result').map((c) => ({ content: c.content, is_error: c.is_error })) : null,
    };
  });
}
console.log(JSON.stringify(out, null, 1));
