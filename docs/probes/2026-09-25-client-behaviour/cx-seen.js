// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// What the model was sent, per turn, out of a cxstub requests.jsonl (Responses API).
// usage: node cx-seen.js <requests.jsonl>
'use strict';
const fs = require('fs');
const lines = fs.readFileSync(process.argv[2], 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l));
for (const d of lines) {
  const b = d.body; const s = JSON.stringify(b);
  const ns = (b.tools || []).filter((t) => t.type === 'namespace').map((t) => ({ name: t.name, description: t.description, tools: (t.tools || []).map((x) => x.name) }));
  const browserNs = ns.filter((n) => /browserai/i.test(n.name));
  const outputs = (b.input || []).filter((i) => i.type === 'function_call_output').map((i) => ({ call_id: i.call_id, output: i.output }));
  const calls = (b.input || []).filter((i) => i.type === 'function_call').map((i) => ({ call_id: i.call_id, namespace: i.namespace, name: i.name }));
  const devBrowser = (b.input || []).filter((i) => i.type === 'message').flatMap((m) => (m.content || []).map((c) => c.text || '')).filter((t) => /browserai|BrowserAI|installing an update|UPDSTUB|MCP/.test(t));
  console.log(JSON.stringify({
    turn: d.turn,
    toolCount: (b.tools || []).length,
    browseraiNamespace: browserNs,
    updateSentenceOccurrences: (s.match(/installing an update/g) || []).length,
    instructionsMarkers: s.match(/UPDSTUB-INSTRUCTIONS launch=\d+ behaves=\w+/g) || [],
    calls,
    outputs,
    messageTextsMentioningBrowserAIorMCP: devBrowser.map((t) => t.slice(0, 600)),
  }));
}
