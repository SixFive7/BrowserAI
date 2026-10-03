// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// One `codex exec` run, summarised from its own files.
// usage: node cx-summary.js <run-dir> <run-name>
'use strict';
const fs = require('fs');
const path = require('path');
const [dir, name] = process.argv.slice(2);
const rd = (p) => { try { return fs.readFileSync(path.join(dir, p), 'utf8'); } catch (e) { return null; } };
const strip = (s) => (s || '').replace(/\x1b\[[0-9;?]*[A-Za-z]/g, '');
const out = { run: name };
const launches = (rd(`logs/${name}.launches.log`) || '').split('\n').filter(Boolean);
out.serverLaunches = launches.filter((l) => / LAUNCH /.test(l)).map((l) => l.replace(/ argv=.*$/, ''));
out.toolsListPerLaunch = {};
for (const l of launches) { const m = l.match(/launch=(\d+) .*tools\/list (REFUSED|answered)/); if (m) out.toolsListPerLaunch[m[1]] = (out.toolsListPerLaunch[m[1]] || []).concat(m[2]); }
out.callsPerLaunch = {};
for (const l of launches) { const m = l.match(/launch=(\d+) .*tools\/call (\S+) (\S+)/); if (m) out.callsPerLaunch[m[1]] = (out.callsPerLaunch[m[1]] || []).concat(`${m[2]} ${m[3]}`); }
out.endOfLaunch1 = launches.filter((l) => /launch=1 .*(ENDING|EXIT|exit code|client closed)/.test(l));
for (const s of [1, 2]) {
  const events = (rd(`out/s${s}.jsonl`) || '').split('\n').filter(Boolean).map((l) => { try { return JSON.parse(l); } catch (e) { return { raw: l }; } });
  const err = strip(rd(`out/s${s}.err.txt`)).split('\n');
  const reqs = (rd(`logs/${name}-s${s}.requests.jsonl`) || '').split('\n').filter(Boolean).map((l) => JSON.parse(l));
  out[`s${s}`] = {
    items: events.filter((e) => e.item).map((e) => ({ type: e.item.type, status: e.item.status, message: e.item.message, text: e.item.text, error: e.item.error, result: e.item.result && e.item.result.content })),
    tracing: err.filter((l) => /MCP server startup failed|failed to initialize MCP client|omitting MCP server|unsupported call|quit_reason|task cancelled|Mcp error/.test(l)).map((l) => l.replace(/^.*?(WARN|ERROR|INFO|DEBUG|TRACE) /, '$1 ').replace(/app_server\.request\{[^}]*\}:/g, '').slice(0, 900)),
    turns: reqs.map((d) => {
      const b = d.body; const str = JSON.stringify(b);
      const nsList = (b.tools || []).filter((t) => t.type === 'namespace');
      return {
        turn: d.turn,
        toolCount: (b.tools || []).length,
        browseraiNamespace: nsList.filter((t) => /browserai/i.test(t.name)).map((t) => ({ name: t.name, description: t.description, tools: (t.tools || []).map((x) => x.name) })),
        updateSentenceOccurrences: (str.match(/installing an update/g) || []).length,
        lastOutput: (b.input || []).filter((i) => i.type === 'function_call_output').slice(-1).map((i) => i.output)[0] || null,
      };
    }),
  };
}
console.log(JSON.stringify(out, null, 1));
