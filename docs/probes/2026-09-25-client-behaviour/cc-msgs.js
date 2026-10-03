// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Every message of one turn's request body, compactly: role, block type, and the
// text of tool_use / tool_result blocks whole. System-reminder text blocks are
// shown only as the reminders' first lines unless --reminders is given.
// usage: node cc-msgs.js <requests.jsonl> <turn> [--reminders]
'use strict';
const fs = require('fs');
const [file, turnArg] = process.argv.slice(2);
const reminders = process.argv.includes('--reminders');
const lines = fs.readFileSync(file, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l));
const d = lines.find((x) => String(x.turn) === String(turnArg));
if (!d) { console.log('no such turn'); process.exit(1); }
for (const [i, m] of d.body.messages.entries()) {
  const parts = Array.isArray(m.content) ? m.content : [{ type: 'text', text: m.content }];
  for (const p of parts) {
    if (p.type === 'text') {
      if (!reminders && /^<system-reminder>/.test(p.text)) {
        const heads = (p.text.match(/<system-reminder>\n[^\n]*/g) || []).map((h) => h.replace('<system-reminder>\n', ''));
        console.log(`[${i}] ${m.role} text(system-reminders): ${JSON.stringify(heads)}`);
      } else {
        console.log(`[${i}] ${m.role} text: ${JSON.stringify(p.text)}`);
      }
    } else if (p.type === 'tool_use') {
      console.log(`[${i}] ${m.role} tool_use: ${JSON.stringify({ id: p.id, name: p.name, input: p.input })}`);
    } else if (p.type === 'tool_result') {
      console.log(`[${i}] ${m.role} tool_result: ${JSON.stringify(p)}`);
    } else {
      console.log(`[${i}] ${m.role} ${p.type}: ${JSON.stringify(p).slice(0, 300)}`);
    }
  }
}
