// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Compares what the probe MCP server published against what Claude Code
// actually sent to the Messages API.
// Usage: node analyse.js <capturedBody.json> [main|bulk]
const fs = require('fs');
const { execFileSync } = require('child_process');

const bodyPath = process.argv[2];
const mode = process.argv[3] || 'main';

const body = JSON.parse(fs.readFileSync(bodyPath, 'utf8'));
const published = JSON.parse(
  execFileSync(process.execPath, [__dirname + '/probe-server.js', mode], {
    env: { ...process.env, PROBE_DUMP: '1' }, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024,
  }));

const B = (s) => Buffer.byteLength(s || '', 'utf8');
const pubByName = new Map(published.tools.map((t) => [t.name, t]));

// Re-derive the exact published strings so we can diff prefixes.
const srcTools = (() => {
  const out = execFileSync(process.execPath, ['-e', `
    process.env.PROBE_DUMP='';
    const m = require(${JSON.stringify((__dirname + '/probe-server.js').replace(/\\/g, '/'))});
  `], { encoding: 'utf8' });
  return out;
})();

function markers(s) {
  return [...String(s || '').matchAll(/MK-[A-Z0-9-]+-9F21/g)].map((m) => m[0]);
}

const rows = [];
for (const tool of body.tools || []) {
  if (!tool.name.startsWith('mcp__probe__')) continue;
  const short = tool.name.replace('mcp__probe__', '');
  const pub = pubByName.get(short);
  const sentDesc = tool.description || '';
  const entry = JSON.stringify(tool);
  const params = Object.entries((tool.input_schema && tool.input_schema.properties) || {})
    .map(([k, v]) => ({ name: k, bytes: B(v.description), chars: (v.description || '').length, markers: markers(v.description) }));
  rows.push({
    tool: short,
    publishedDescBytes: pub ? pub.descriptionBytes : null,
    sentDescBytes: B(sentDesc),
    publishedDescChars: pub ? pub.descriptionChars : null,
    sentDescChars: sentDesc.length,
    descTruncated: pub ? B(sentDesc) !== pub.descriptionBytes : null,
    descMarkers: markers(sentDesc),
    descTail: JSON.stringify(sentDesc.slice(-70)),
    publishedEntryBytes: pub ? pub.entryBytes : null,
    sentEntryBytes: B(entry),
    params,
  });
}

console.log('=== Claude Code -> Messages API, probe tools ===');
console.log('request body bytes:', fs.statSync(bodyPath).size);
console.log('total tools in request:', (body.tools || []).length);
console.log('sum of all tool entries (bytes):', (body.tools || []).reduce((a, t) => a + B(JSON.stringify(t)), 0));
console.log('');
for (const r of rows) {
  console.log(`--- ${r.tool}`);
  console.log(`    description: published ${r.publishedDescBytes} B / ${r.publishedDescChars} c  ->  sent ${r.sentDescBytes} B / ${r.sentDescChars} c  ${r.descTruncated ? '*** TRUNCATED ***' : '(intact)'}`);
  console.log(`    desc markers surviving: ${r.descMarkers.join(' ') || '(none)'}`);
  console.log(`    desc tail: ${r.descTail}`);
  console.log(`    whole entry: published ${r.publishedEntryBytes} B  ->  sent ${r.sentEntryBytes} B`);
  for (const p of r.params) {
    const pubBytes = pubByName.get(r.tool) ? pubByName.get(r.tool).params[p.name] : null;
    console.log(`      param ${p.name}: published ${pubBytes} B -> sent ${p.bytes} B / ${p.chars} c ${pubBytes !== null && pubBytes !== p.bytes ? '*** TRUNCATED ***' : ''}  markers: ${p.markers.join(' ') || '(none)'}`);
  }
  console.log('');
}

// Server instructions: find where they landed.
const sys = JSON.stringify(body.system || '');
const instrMarkers = markers(sys);
console.log('=== server instructions ===');
console.log('published:', published.instructionsBytes, 'B /', published.instructionsChars, 'c');
console.log('markers found anywhere in system prompt:', instrMarkers.join(' ') || '(none)');
const msgs = JSON.stringify(body.messages || '');
console.log('markers found in messages:', markers(msgs).join(' ') || '(none)');
