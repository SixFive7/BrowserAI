// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Round three. One question: when the 2048th UTF-16 unit falls in the middle of
// a surrogate pair, does the cut leave a lone surrogate?
// Construction is asserted locally before it is ever published.
const S = '9F21';
const EMOJI = '\u{1F600}';

// 21 ASCII characters (odd), then nothing but emoji. Pair j occupies indices
// 21+2j and 21+2j+1, so index 2047 (2047-21 = 2026, even) is a HIGH surrogate.
const PREFIX = 'MK-SPLIT2-START-' + S + 'x'; // 20 + 1 = 21
let split = PREFIX;
while (split.length < 2980) split += EMOJI;
split += 'MK-SPLIT2-END-' + S;

const hi = split.charCodeAt(2047);
if (!(hi >= 0xD800 && hi <= 0xDBFF)) {
  throw new Error('construction failed: index 2047 is U+' + hi.toString(16) + ', not a high surrogate');
}

const TOOLS = [
  { name: 'probe_control3', description: 'Round three control. MK-CTRL3-END-' + S, inputSchema: { type: 'object', properties: {} } },
  { name: 'probe_split_true', description: split, inputSchema: { type: 'object', properties: {} } },
];

if (process.env.PROBE_DUMP) {
  process.stdout.write(JSON.stringify({
    splitUnits: split.length,
    splitCodePoints: [...split].length,
    splitBytes: Buffer.byteLength(split, 'utf8'),
    unitAt2047: 'U+' + hi.toString(16),
    unitAt2048: 'U+' + split.charCodeAt(2048).toString(16),
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
        serverInfo: { name: 'truncation-probe3', version: '1.0.0' },
      } });
      break;
    case 'tools/list': send({ jsonrpc: '2.0', id, result: { tools: TOOLS } }); break;
    case 'tools/call': send({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text: 'ok' }], isError: false } }); break;
    case 'ping': send({ jsonrpc: '2.0', id, result: {} }); break;
    case 'resources/list': send({ jsonrpc: '2.0', id, result: { resources: [] } }); break;
    case 'prompts/list': send({ jsonrpc: '2.0', id, result: { prompts: [] } }); break;
    default: send({ jsonrpc: '2.0', id, error: { code: -32601, message: 'Method not found' } });
  }
}
