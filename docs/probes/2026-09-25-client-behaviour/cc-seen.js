// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// What the model was sent, per turn, out of a ccstub requests.jsonl.
// usage: node cc-seen.js <requests.jsonl> [--full]
'use strict';
const fs = require('fs');
const file = process.argv[2];
const full = process.argv.includes('--full');
const lines = fs.readFileSync(file, 'utf8').split('\n').filter(Boolean);
for (const l of lines) {
  const d = JSON.parse(l);
  const b = d.body;
  const s = JSON.stringify(b);
  const tools = (b.tools || []).map((t) => t.name);
  const instr = s.match(/UPDSTUB-INSTRUCTIONS[^"\\]*/g) || [];
  const upd = (s.match(/installing an update/g) || []).length;
  const browserTools = tools.filter((n) => /browserai/i.test(n));
  console.log(`turn ${d.turn} at ${d.at}: tools=${tools.length} browserai-tools=${JSON.stringify(browserTools)} instructions-marker=${JSON.stringify(instr)} "installing an update" x${upd}`);
  // Where do BrowserAI's instructions appear, if at all?
  const sys = typeof b.system === 'string' ? b.system : JSON.stringify(b.system || '');
  const sysHasMcp = /MCP Server Instructions/.test(sys);
  console.log(`   system has "MCP Server Instructions": ${sysHasMcp}`);
  const msgs = b.messages || [];
  const last = msgs[msgs.length - 1];
  const contents = Array.isArray(last.content) ? last.content : [{ type: 'text', text: last.content }];
  for (const c of contents) {
    if (c && c.type === 'tool_result') console.log(`   last tool_result: ${JSON.stringify(c)}`);
    else if (c && c.type === 'text' && full) console.log(`   last text: ${JSON.stringify(c.text).slice(0, 3000)}`);
  }
  if (full) {
    // Every text block anywhere in the messages that mentions browserai, whole.
    for (const [i, m] of msgs.entries()) {
      const parts = Array.isArray(m.content) ? m.content : [{ type: 'text', text: m.content }];
      for (const p of parts) {
        const t = JSON.stringify(p);
        if (/browserai|BrowserAI|UPDSTUB|MCP server/i.test(t)) console.log(`   msg[${i}] ${m.role}: ${t.slice(0, 4000)}`);
      }
    }
  }
}
