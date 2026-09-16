// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A minimal MCP stdio server whose only job is to publish strings of known,
// exact byte length carrying unique end-markers. Not part of the BrowserAI product.
// Usage: node probe-server.js [main|bulk]
const MODE = process.argv[2] || 'main';
const S = '9F21';

const FILLER =
  'The quick brown fox jumps over the lazy dog while this budget experiment measures exactly where the client cuts a string. ';

// Exact-length ASCII, with each marker ending exactly at byte offset `at`.
function ascii(targetBytes, marks) {
  const base = FILLER.repeat(Math.ceil(targetBytes / FILLER.length) + 2).slice(0, targetBytes);
  const arr = base.split('');
  for (const m of marks) {
    const start = m.at - m.text.length;
    if (start < 0) throw new Error('marker before start: ' + m.text);
    for (let i = 0; i < m.text.length; i++) arr[start + i] = m.text[i];
  }
  return arr.join('');
}

const B = (s) => Buffer.byteLength(s, 'utf8');

const EMDASH = '—'; // 3 UTF-8 bytes, 1 UTF-16 unit, 1 code point

// Multi-byte string. Markers are ASCII, so a marker's own bytes == its own chars.
// Byte offsets and character offsets diverge 3:1 through the em-dash run.
function multibyte() {
  const marks = [
    { atByte: 300, text: 'MK-MB-START-' + S },
    { atByte: 1900, text: 'MK-MB-B1900-' + S },   // under 2048 bytes AND under 2048 chars
    { atByte: 2500, text: 'MK-MB-B2500-' + S },   // OVER 2048 bytes, UNDER 2048 chars -> decisive
    { atByte: 4000, text: 'MK-MB-B4000-' + S },   // over bytes, still under chars
    { atByte: 6400, text: 'MK-MB-B6400-' + S },   // over bytes AND over 2048 chars
    { atByte: 7600, text: 'MK-MB-END-' + S },
  ];
  let s = '';
  for (const m of marks) {
    while (B(s) + B(m.text) + 2 < m.atByte) s += EMDASH;
    while (B(s) + B(m.text) < m.atByte) s += 'x';
    s += m.text;
  }
  while (B(s) < 7800) s += EMDASH;
  return s;
}

const INSTRUCTIONS = ascii(2600, [
  { at: 40, text: 'MK-INSTR-START-' + S },
  { at: 1000, text: 'MK-INSTR-AT1000-' + S },
  { at: 2000, text: 'MK-INSTR-AT2000-' + S },
  { at: 2100, text: 'MK-INSTR-AT2100-' + S },
  { at: 2600, text: 'MK-INSTR-END-' + S },
]);

function mainTools() {
  const tools = [];

  // 1. Control. Must be visible in every valid run, or the run is discarded.
  tools.push({
    name: 'probe_control',
    description: 'Control tool, deliberately tiny. MK-CTRL-END-' + S,
    inputSchema: {
      type: 'object',
      properties: { note: { type: 'string', description: 'Control parameter, tiny. MK-CTRLPARAM-END-' + S } },
      required: [],
    },
  });

  // 2. Description comfortably under 2048 B, and the whole entry under 2048 B too.
  tools.push({
    name: 'probe_desc_under',
    description: ascii(1850, [
      { at: 30, text: 'MK-DESCUNDER-START-' + S },
      { at: 1850, text: 'MK-DESCUNDER-END-' + S },
    ]),
    inputSchema: { type: 'object', properties: { note: { type: 'string' } }, required: [] },
  });

  // 3. Description over 2048 B; markers bracket the cut.
  tools.push({
    name: 'probe_desc_over',
    description: ascii(2600, [
      { at: 30, text: 'MK-DESCOVER-START-' + S },
      { at: 1000, text: 'MK-DESCOVER-AT1000-' + S },
      { at: 2000, text: 'MK-DESCOVER-AT2000-' + S },
      { at: 2100, text: 'MK-DESCOVER-AT2100-' + S },
      { at: 2600, text: 'MK-DESCOVER-END-' + S },
    ]),
    inputSchema: { type: 'object', properties: { note: { type: 'string' } }, required: [] },
  });

  // 4. THE DECISIVE ONE. Every string under 2048 B; the whole entry ~4.6 KB.
  tools.push({
    name: 'probe_entry_over',
    description: ascii(1500, [
      { at: 30, text: 'MK-ENTRY-DESC-START-' + S },
      { at: 1500, text: 'MK-ENTRY-DESC-END-' + S },
    ]),
    inputSchema: {
      type: 'object',
      properties: {
        p1: { type: 'string', description: ascii(700, [{ at: 700, text: 'MK-ENTRY-P1-END-' + S }]) },
        p2: { type: 'string', description: ascii(700, [{ at: 700, text: 'MK-ENTRY-P2-END-' + S }]) },
        p3: { type: 'string', description: ascii(700, [{ at: 700, text: 'MK-ENTRY-P3-END-' + S }]) },
        p4: { type: 'string', description: ascii(700, [{ at: 700, text: 'MK-ENTRY-P4-END-' + S }]) },
      },
      required: [],
    },
  });

  // 5. Is a PARAMETER description capped per-string at all?
  tools.push({
    name: 'probe_param_over',
    description: 'Tiny tool description on purpose. MK-PARAMOVER-DESC-END-' + S,
    inputSchema: {
      type: 'object',
      properties: {
        big: {
          type: 'string',
          description: ascii(2600, [
            { at: 30, text: 'MK-PARAMOVER-START-' + S },
            { at: 1000, text: 'MK-PARAMOVER-AT1000-' + S },
            { at: 2000, text: 'MK-PARAMOVER-AT2000-' + S },
            { at: 2100, text: 'MK-PARAMOVER-AT2100-' + S },
            { at: 2600, text: 'MK-PARAMOVER-END-' + S },
          ]),
        },
        small: { type: 'string', description: 'Small parameter control. MK-PARAMSMALL-END-' + S },
      },
      required: [],
    },
  });

  // 6. Bytes or characters?
  tools.push({
    name: 'probe_multibyte',
    description: multibyte(),
    inputSchema: { type: 'object', properties: { note: { type: 'string' } }, required: [] },
  });

  return tools;
}

function bulkTools() {
  const tools = mainTools();
  for (let i = 0; i < 60; i++) {
    const n = String(i).padStart(2, '0');
    tools.push({
      name: 'probe_bulk_' + n,
      description: ascii(1200, [
        { at: 30, text: 'MK-BULK-' + n + '-START-' + S },
        { at: 1200, text: 'MK-BULK-' + n + '-END-' + S },
      ]),
      inputSchema: { type: 'object', properties: { note: { type: 'string' } }, required: [] },
    });
  }
  return tools;
}

const TOOLS = MODE === 'bulk' ? bulkTools() : mainTools();

if (process.env.PROBE_DUMP) {
  const out = {
    mode: MODE,
    instructionsBytes: B(INSTRUCTIONS),
    instructionsChars: INSTRUCTIONS.length,
    wholeToolsArrayBytes: B(JSON.stringify(TOOLS)),
    toolCount: TOOLS.length,
    tools: TOOLS.map((t) => ({
      name: t.name,
      descriptionBytes: B(t.description),
      descriptionChars: t.description.length,
      entryBytes: B(JSON.stringify(t)),
      params: Object.fromEntries(
        Object.entries(t.inputSchema.properties).map(([k, v]) => [k, v.description ? B(v.description) : 0])),
    })),
  };
  process.stdout.write(JSON.stringify(out, null, 2) + '\n');
  process.exit(0);
}

let buf = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (d) => {
  buf += d;
  let i;
  while ((i = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, i).trim();
    buf = buf.slice(i + 1);
    if (line) handle(line);
  }
});

function send(obj) {
  process.stdout.write(JSON.stringify(obj) + '\n');
}

function handle(line) {
  let msg;
  try { msg = JSON.parse(line); } catch { return; }
  if (msg.id === undefined || msg.id === null) return; // notification
  const id = msg.id;
  const method = msg.method;
  if (method === 'initialize') {
    send({
      jsonrpc: '2.0', id,
      result: {
        protocolVersion: (msg.params && msg.params.protocolVersion) || '2025-06-18',
        capabilities: { tools: { listChanged: false } },
        serverInfo: { name: 'truncation-probe', version: '1.0.0' },
        instructions: INSTRUCTIONS,
      },
    });
  } else if (method === 'tools/list') {
    send({ jsonrpc: '2.0', id, result: { tools: TOOLS } });
  } else if (method === 'tools/call') {
    send({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text: 'probe ok' }], isError: false } });
  } else if (method === 'ping') {
    send({ jsonrpc: '2.0', id, result: {} });
  } else if (method === 'resources/list') {
    send({ jsonrpc: '2.0', id, result: { resources: [] } });
  } else if (method === 'prompts/list') {
    send({ jsonrpc: '2.0', id, result: { prompts: [] } });
  } else {
    send({ jsonrpc: '2.0', id, error: { code: -32601, message: 'Method not found' } });
  }
}
