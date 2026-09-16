// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Round two. Pins the boundary predicate, distinguishes UTF-16 code units from
// Unicode code points, and looks for ANY larger cap on a parameter description
// or a whole tool entry. Not part of the BrowserAI product.
const S = '9F21';
const FILLER =
  'The quick brown fox jumps over the lazy dog while this budget experiment measures exactly where the client cuts a string. ';

function ascii(targetChars, marks) {
  const base = FILLER.repeat(Math.ceil(targetChars / FILLER.length) + 2).slice(0, targetChars);
  const arr = base.split('');
  for (const m of marks || []) {
    const start = m.at - m.text.length;
    for (let i = 0; i < m.text.length; i++) arr[start + i] = m.text[i];
  }
  return arr.join('');
}

const EMOJI = '\u{1F600}'; // 4 UTF-8 bytes, 2 UTF-16 units, 1 code point

// `prefix` ASCII chars, then emoji; markers end at the given UTF-16 index.
function astral(targetUnits, prefixChars, marks) {
  let s = 'x'.repeat(prefixChars);
  for (const m of marks || []) {
    while (s.length + m.text.length < m.at - 1) s += EMOJI;
    while (s.length + m.text.length < m.at) s += 'x';
    s += m.text;
  }
  while (s.length < targetUnits - 1) s += EMOJI;
  return s;
}

const TOOLS = [
  {
    name: 'probe_control2',
    description: 'Round two control tool. MK-CTRL2-END-' + S,
    inputSchema: { type: 'object', properties: {}, required: [] },
  },

  // --- The boundary predicate: is it > 2048 or >= 2048? -------------------
  { name: 'probe_desc_2047', description: ascii(2047, [{ at: 2047, text: 'MK-B2047-END-' + S }]), inputSchema: { type: 'object', properties: {} } },
  { name: 'probe_desc_2048', description: ascii(2048, [{ at: 2048, text: 'MK-B2048-END-' + S }]), inputSchema: { type: 'object', properties: {} } },
  { name: 'probe_desc_2049', description: ascii(2049, [{ at: 2049, text: 'MK-B2049-END-' + S }]), inputSchema: { type: 'object', properties: {} } },

  // --- UTF-16 code units or Unicode code points? -------------------------
  // ~3000 UTF-16 units but only ~1500 code points. Truncated => units.
  {
    name: 'probe_astral_aligned',
    description: astral(3000, 0, [
      { at: 40, text: 'MK-ASTRAL-START-' + S },
      { at: 2000, text: 'MK-ASTRAL-U2000-' + S },
      { at: 2100, text: 'MK-ASTRAL-U2100-' + S },
      { at: 2900, text: 'MK-ASTRAL-END-' + S },
    ]),
    inputSchema: { type: 'object', properties: {} },
  },
  // Same, but offset by one so UTF-16 index 2047 is a HIGH surrogate: a naive
  // slice(0, 2048) leaves a lone surrogate at the cut.
  {
    name: 'probe_astral_split',
    description: astral(3000, 1, [
      { at: 40, text: 'MK-SPLIT-START-' + S },
      { at: 2000, text: 'MK-SPLIT-U2000-' + S },
      { at: 2900, text: 'MK-SPLIT-END-' + S },
    ]),
    inputSchema: { type: 'object', properties: {} },
  },

  // --- Is there ANY cap on a parameter description? ----------------------
  {
    name: 'probe_param_huge',
    description: 'Tiny on purpose. MK-PARAMHUGE-DESC-' + S,
    inputSchema: {
      type: 'object',
      properties: {
        huge: { type: 'string', description: ascii(20000, [
          { at: 30, text: 'MK-PARAMHUGE-START-' + S },
          { at: 2048, text: 'MK-PARAMHUGE-AT2048-' + S },
          { at: 10000, text: 'MK-PARAMHUGE-AT10000-' + S },
          { at: 20000, text: 'MK-PARAMHUGE-END-' + S },
        ]) },
      },
    },
  },

  // --- Is there ANY cap on a whole tool entry? ---------------------------
  // Eight parameters of 2000 chars each: every string under 2048, entry ~17 KB.
  {
    name: 'probe_entry_huge',
    description: ascii(1000, [{ at: 1000, text: 'MK-EHUGE-DESC-END-' + S }]),
    inputSchema: {
      type: 'object',
      properties: Object.fromEntries(
        Array.from({ length: 8 }, (_, i) => [
          'q' + i,
          { type: 'string', description: ascii(2000, [{ at: 2000, text: 'MK-EHUGE-Q' + i + '-END-' + S }]) },
        ])),
    },
  },
];

const INSTRUCTIONS = 'Round two probe. MK-INSTR2-END-' + S;

const B = (s) => Buffer.byteLength(s, 'utf8');

if (process.env.PROBE_DUMP) {
  process.stdout.write(JSON.stringify({
    mode: 'wide',
    instructionsBytes: B(INSTRUCTIONS),
    instructionsChars: INSTRUCTIONS.length,
    wholeToolsArrayBytes: B(JSON.stringify(TOOLS)),
    toolCount: TOOLS.length,
    tools: TOOLS.map((t) => ({
      name: t.name,
      descriptionBytes: B(t.description),
      descriptionChars: t.description.length,
      descriptionCodePoints: [...t.description].length,
      entryBytes: B(JSON.stringify(t)),
      params: Object.fromEntries(Object.entries(t.inputSchema.properties || {})
        .map(([k, v]) => [k, v.description ? v.description.length : 0])),
    })),
  }, null, 2) + '\n');
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

const send = (o) => process.stdout.write(JSON.stringify(o) + '\n');

function handle(line) {
  let msg;
  try { msg = JSON.parse(line); } catch { return; }
  if (msg.id === undefined || msg.id === null) return;
  const id = msg.id;
  switch (msg.method) {
    case 'initialize':
      send({ jsonrpc: '2.0', id, result: {
        protocolVersion: (msg.params && msg.params.protocolVersion) || '2025-06-18',
        capabilities: { tools: { listChanged: false } },
        serverInfo: { name: 'truncation-probe2', version: '1.0.0' },
        instructions: INSTRUCTIONS,
      } });
      break;
    case 'tools/list': send({ jsonrpc: '2.0', id, result: { tools: TOOLS } }); break;
    case 'tools/call': send({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text: 'probe ok' }], isError: false } }); break;
    case 'ping': send({ jsonrpc: '2.0', id, result: {} }); break;
    case 'resources/list': send({ jsonrpc: '2.0', id, result: { resources: [] } }); break;
    case 'prompts/list': send({ jsonrpc: '2.0', id, result: { prompts: [] } }); break;
    default: send({ jsonrpc: '2.0', id, error: { code: -32601, message: 'Method not found' } });
  }
}
