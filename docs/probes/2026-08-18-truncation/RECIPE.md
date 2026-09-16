<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# Claude Code's "2KB each" truncation: measured, and what it means for your guard

**Read this if your MCP server has a check, a lint or a habit that keeps tool
descriptions under 2 KB.** Yours is probably calibrated on a guess. Mine was. The
guess is now unnecessary — here is the measurement, and the recipe to re-run it
yourself in about fifteen minutes at zero cost.

You do not need to know anything about the project this came from.

---

## The problem, stated

Claude Code's [MCP documentation](https://code.claude.com/docs/en/mcp) says,
verbatim:

> Claude Code truncates tool descriptions and server instructions at 2KB each.
> Keep them concise to avoid truncation, and put critical details near the start.

**"Each" does not say each *what*.** Two readings, and they imply opposite fixes:

- **Per string** — every `description` and the `instructions` block get their own
  2 KB. Trimming a description helps.
- **Per serialized tool** — the whole `tools/list` entry (name + description +
  the entire `inputSchema` including every parameter description) shares one 2 KB
  bucket. Under this reading, **trimming a description just moves text from one
  capped bucket into the same capped bucket** and fixes nothing.

If you have ever trimmed a description and had the problem persist, you have met
the second reading.

---

## The answer

**Measured 2026-08-18 against Claude Code `2.1.234` on Windows 11.** Read off the
client's own outbound `POST /v1/messages` request — the actual `tools` array the
model receives — not off a model's recollection. Reproduced twice, against
`sonnet` and `haiku`, byte-identical, because the cut is client-side.

| Question | Answer |
|---|---|
| Per string or per tool? | **Per string.** There is no per-tool bucket at all |
| Unit? | **UTF-16 code units** (JavaScript `String.length`). **Bytes are never counted** |
| Exact boundary? | Cut when `length > 2048`; truncated to 2,048 |
| Code units or code points? | **Code units**, and the cut is surrogate-aware |
| Are `inputSchema.properties[*].description` capped? | **No. Not at all** |
| Total budget across `tools/list`? | **No** |
| What does a cut look like? | Hard positional cut, then the literal **`… [truncated]`** appended |

### The evidence, probe by probe

**Per string, not per tool.** A probe tool published a 1,500-character
description plus four 700-character parameter descriptions — every individual
string well under the cap, whole serialized entry **4,578 bytes**. It arrived
**completely intact**. Pushed further: an entry of **17,411 bytes** (eight
2,000-character parameter descriptions) and one of **20,172 bytes** (a single
20,000-character parameter description) also arrived intact.

**Characters, not bytes.** Two descriptions were cut at the *same character
offset* despite very different byte lengths:

| Probe | Published | Delivered | Cut at |
|---|---|---|---|
| ASCII filler | 2,600 chars / 2,600 bytes | 2,061 chars | 2,048 **characters** |
| Em-dash filler | 2,669 chars / **7,801 bytes** | 2,061 chars | 2,048 **characters** = 6,004 bytes |

**A 2,048-character description weighing 6,004 bytes arrives whole.** If your
guard counts UTF-8 bytes, it is rejecting text the client would have delivered.

**The boundary, as a triple in one run:** 2,047 intact · **2,048 intact** ·
2,049 cut. The predicate is `> 2048`.

**Code units, not code points.** A description of 1,539 code points spread over
3,000 UTF-16 units (emoji) was cut — at unit 2,048, which is code point 1,044.
Under a code-point cap it would have arrived whole.

**Surrogate-aware.** A probe was built so that UTF-16 index 2,047 is a *high*
surrogate and 2,048 its *low* partner (asserted in the probe before publishing,
so it is not a lucky alignment). A naive `slice(0, 2048)` would leave a lone
surrogate. The client cut to **2,047** instead and the delivered payload is
well-formed. So: *cut to the largest offset ≤ 2,048 units that does not split a
surrogate pair.*

**Parameter descriptions are uncapped.** 2,600 characters through intact;
**20,000 characters** through intact. The documentation's silence about
`inputSchema` is accurate, not an omission.

**No total budget.** 202 tools totalling **348,314 bytes** of serialized entries
in one request (body 392,983 bytes): nothing dropped, nothing cut, every marker
present including the last tool of the last server.

**What a cut looks like.** The delivered string is the published string's exact
prefix followed by the literal `… [truncated]` — U+2026 HORIZONTAL ELLIPSIS,
space, `[truncated]` — **13 characters, 15 bytes**. So a truncated string arrives
at **2,061 characters**. Nothing is dropped wholesale: not the field, not the
tool.

⚠️ **The suffix is visible to the model and invisible to your server.** It is
added after your JSON-RPC response has left. There is no error, no notification,
no re-request. **A server cannot detect its own truncation.** A *model* can — so
"did that arrive whole?" is answerable by asking, and unanswerable by logging.

---

## What this means for your guard

1. **If your guard assumes per-string: it was right.** Keep it. You do not need
   to split large tools, flatten schemas, or move parameter documentation out of
   the schema.

2. **If your guard counts UTF-8 bytes: it is miscalibrated in the safe
   direction.** For UTF-8 the byte count is never below the character count, so a
   byte gate can only produce *false* failures — but it will reject a
   2,000-character description carrying em dashes, curly quotes or emoji that the
   client delivers whole. Switch to `String.length` / UTF-16 code units. That is
   not a relaxation; it is the end of a guess.

3. **If your guard also caps parameter descriptions: the client does not.** Keep
   the cap if you want — I did — but **relabel it a house limit, not a client
   limit**, so nobody later cites it as documented. My reason for keeping it: it
   floats with a client version I do not control, and one shared parameter
   description injected across dozens of tools would become dozens of silent
   truncations the day a release starts cutting schemas.

4. **Stop worrying about whole-entry totals.** They are not a budget. Keep
   *reporting* them if it is cheap — that is the number a future per-tool bucket
   would be judged against — but do not fail a build on them.

5. **Boundary off-by-one:** a description of exactly 2,048 is fine. 2,049 is not.

6. **This is one client's implementation detail, not a protocol rule.** Cursor,
   Windsurf, Zed and the Claude desktop app were not tested and will not
   necessarily agree. The *method* below transfers; the numbers do not.

---

## Re-run it yourself

Fifteen minutes, no cost, no real API call, no real credential. Everything runs
on `127.0.0.1`.

**The trick:** Claude Code honours `ANTHROPIC_BASE_URL`. Point it at a local HTTP
server and you get the full request body — including the `tools` array byte for
byte — on disk. Answer with a minimal SSE stream and the client is satisfied and
exits cleanly. The model never runs, so nothing depends on it complying,
paraphrasing, or refusing.

**Auth, both parts learned the hard way:**

- `ANTHROPIC_API_KEY` with a made-up key **does not work** — the client prints
  `Not logged in · Please run /login` and never sends `/v1/messages`. (It still
  sends `HEAD /api/hello` to your base URL, which is a handy proof that the
  interception itself works.)
- **`ANTHROPIC_AUTH_TOKEN` with any throwaway string works immediately.** Use
  that.

### 1. The capture server — `capture.js`

```js
const http = require('http'), fs = require('fs'), path = require('path');
const outDir = process.argv[2], port = Number(process.argv[3] || 8787);
fs.mkdirSync(outDir, { recursive: true });
let n = 0;
http.createServer((req, res) => {
  const chunks = [];
  req.on('data', c => chunks.push(c));
  req.on('end', () => {
    const body = Buffer.concat(chunks);
    n += 1;
    const id = String(n).padStart(3, '0');
    // NEVER write credentials to disk.
    const headers = {};
    for (const [k, v] of Object.entries(req.headers))
      headers[k] = /^(authorization|x-api-key|cookie)$/i.test(k) ? '<redacted>' : v;
    fs.writeFileSync(path.join(outDir, `req-${id}.json`),
      JSON.stringify({ seq: n, method: req.method, url: req.url, headers, bodyBytes: body.length }, null, 2));
    if (body.length) fs.writeFileSync(path.join(outDir, `body-${id}.json`), body);
    console.error(`captured ${req.method} ${req.url} ${body.length}B`);

    if (req.url.includes('count_tokens')) {
      res.writeHead(200, { 'content-type': 'application/json' });
      return res.end(JSON.stringify({ input_tokens: 1 }));
    }
    if (!body.toString('utf8').includes('"stream":true')) {
      res.writeHead(200, { 'content-type': 'application/json' });
      return res.end(JSON.stringify({ id: 'msg_probe', type: 'message', role: 'assistant',
        model: 'probe', content: [{ type: 'text', text: 'CAPTURED' }],
        stop_reason: 'end_turn', stop_sequence: null, usage: { input_tokens: 1, output_tokens: 1 } }));
    }
    res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' });
    const ev = (t, d) => res.write(`event: ${t}\ndata: ${JSON.stringify(d)}\n\n`);
    ev('message_start', { type: 'message_start', message: { id: 'msg_probe', type: 'message',
      role: 'assistant', model: 'probe', content: [], stop_reason: null, stop_sequence: null,
      usage: { input_tokens: 1, output_tokens: 1 } } });
    ev('content_block_start', { type: 'content_block_start', index: 0, content_block: { type: 'text', text: '' } });
    ev('content_block_delta', { type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text: 'CAPTURED' } });
    ev('content_block_stop', { type: 'content_block_stop', index: 0 });
    ev('message_delta', { type: 'message_delta', delta: { stop_reason: 'end_turn', stop_sequence: null }, usage: { output_tokens: 1 } });
    ev('message_stop', { type: 'message_stop' });
    res.end();
  });
}).listen(port, '127.0.0.1', () => console.error(`capture on 127.0.0.1:${port}`));
```

### 2. The probe MCP server — `probe.js`

Raw JSON-RPC over stdio; no SDK, so there is nothing to install.

```js
const S = 'MK9F21';
const FILLER = 'The quick brown fox jumps over the lazy dog while this experiment measures where the client cuts a string. ';
// Exact-length ASCII with each marker ENDING at character offset `at`.
function ascii(len, marks = []) {
  const arr = FILLER.repeat(Math.ceil(len / FILLER.length) + 2).slice(0, len).split('');
  for (const m of marks) for (let i = 0; i < m.text.length; i++) arr[m.at - m.text.length + i] = m.text[i];
  return arr.join('');
}
const TOOLS = [
  // CONTROL: must be visible or the run is void.
  { name: 'probe_control', description: `Tiny control. CTRL-END-${S}`,
    inputSchema: { type: 'object', properties: {} } },
  // Boundary triple.
  { name: 'probe_2047', description: ascii(2047, [{ at: 2047, text: `B2047-END-${S}` }]), inputSchema: { type: 'object', properties: {} } },
  { name: 'probe_2048', description: ascii(2048, [{ at: 2048, text: `B2048-END-${S}` }]), inputSchema: { type: 'object', properties: {} } },
  { name: 'probe_2049', description: ascii(2049, [{ at: 2049, text: `B2049-END-${S}` }]), inputSchema: { type: 'object', properties: {} } },
  // THE DECISIVE ONE: every string under the cap, whole entry ~4.6 KB.
  { name: 'probe_entry_over', description: ascii(1500, [{ at: 1500, text: `EDESC-END-${S}` }]),
    inputSchema: { type: 'object', properties: {
      p1: { type: 'string', description: ascii(700, [{ at: 700, text: `EP1-END-${S}` }]) },
      p2: { type: 'string', description: ascii(700, [{ at: 700, text: `EP2-END-${S}` }]) },
      p3: { type: 'string', description: ascii(700, [{ at: 700, text: `EP3-END-${S}` }]) },
      p4: { type: 'string', description: ascii(700, [{ at: 700, text: `EP4-END-${S}` }]) } } } },
  // Are parameter descriptions capped at all?
  { name: 'probe_param_huge', description: `Tiny on purpose. PHDESC-${S}`,
    inputSchema: { type: 'object', properties: {
      huge: { type: 'string', description: ascii(20000, [{ at: 20000, text: `PHUGE-END-${S}` }]) } } } },
  // Bytes or characters? 2,669 chars but 7,801 bytes.
  { name: 'probe_multibyte', description: '—'.repeat(2600) + `MB-END-${S}`,
    inputSchema: { type: 'object', properties: {} } },
];
const INSTRUCTIONS = ascii(2600, [{ at: 2000, text: `INSTR-AT2000-${S}` }, { at: 2600, text: `INSTR-END-${S}` }]);

let buf = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', d => {
  buf += d;
  let i;
  while ((i = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, i).trim(); buf = buf.slice(i + 1);
    if (!line) continue;
    let m; try { m = JSON.parse(line); } catch { continue; }
    if (m.id === undefined || m.id === null) continue;      // notification
    const send = r => process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: m.id, result: r }) + '\n');
    if (m.method === 'initialize') send({ protocolVersion: (m.params && m.params.protocolVersion) || '2025-06-18',
      capabilities: { tools: { listChanged: false } }, serverInfo: { name: 'probe', version: '1.0.0' },
      instructions: INSTRUCTIONS });
    else if (m.method === 'tools/list') send({ tools: TOOLS });
    else if (m.method === 'tools/call') send({ content: [{ type: 'text', text: 'ok' }], isError: false });
    else if (m.method === 'resources/list') send({ resources: [] });
    else if (m.method === 'prompts/list') send({ prompts: [] });
    else if (m.method === 'ping') send({});
    else process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: m.id,
      error: { code: -32601, message: 'Method not found' } }) + '\n');
  }
});
```

### 3. Run it

⚠️ **`CLAUDE_CONFIG_DIR` must point at a scratch directory.** Without it,
`claude mcp add` writes into `~/.claude.json` — your own MCP configuration.

```bash
mkdir -p /tmp/probe/cfg /tmp/probe/cap /tmp/probe/proj
node capture.js /tmp/probe/cap 8787 &

export CLAUDE_CONFIG_DIR=/tmp/probe/cfg
claude mcp add probe --scope user -- "$(which node)" "$PWD/probe.js"

cd /tmp/probe/proj
ANTHROPIC_BASE_URL=http://127.0.0.1:8787 \
ANTHROPIC_AUTH_TOKEN=throwaway \
claude -p "Reply with the single word OK." --model sonnet
```

You should see `CAPTURED` — that is your fake endpoint's reply, which means the
request was intercepted. Afterwards, confirm your real config was untouched:
`grep probe ~/.claude.json` must find nothing.

### 4. Read the answer

```js
// node analyse.js /tmp/probe/cap/body-004.json   (pick the LARGEST body-*.json)
const fs = require('fs');
const SUF = '… [truncated]';
const b = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const B = s => Buffer.byteLength(s || '', 'utf8');
console.log('tools in request:', b.tools.length,
            '| sum of entries:', b.tools.reduce((a, t) => a + B(JSON.stringify(t)), 0), 'B');
for (const t of b.tools.filter(t => t.name.includes('probe'))) {
  const d = t.description || '', cut = d.endsWith(SUF);
  console.log(`${t.name}\n  desc ${d.length} units / ${B(d)} B  truncated=${cut}` +
              (cut ? `  core=${d.length - SUF.length}` : '') +
              `\n  whole entry ${B(JSON.stringify(t))} B`);
  for (const [k, v] of Object.entries((t.input_schema || {}).properties || {}))
    console.log(`    param ${k}: ${(v.description || '').length} units  truncated=${(v.description || '').endsWith(SUF)}`);
}
```

**What you should see if the finding still holds at your client version:**

- `probe_control` intact ← *if this is missing, the run is void; fix registration
  before reading anything else*
- `probe_2047` and `probe_2048` intact, `probe_2049` cut to 2,061 units
- `probe_entry_over` intact — description and all four parameters — with a whole
  entry around 4.6 KB
- `probe_param_huge.huge` intact at 20,000 units
- `probe_multibyte` cut to 2,061 units (≈ 6 KB), proving characters not bytes

Any deviation means the client changed. Re-measure rather than reason about it.

### 5. Clean up

```bash
kill %1                        # the capture server
rm -rf /tmp/probe              # scratch config, captures, probes
```

---

## Caveats, stated rather than buried

- **One client, one version.** Claude Code `2.1.234`, Windows 11 Pro 26200,
  Node v26.7.0. Nothing here is a protocol guarantee and nothing watches it for
  you — there is no version header, no notification and no server-side signal when
  it changes. Put a re-check on whatever list you re-work at dependency-bump time.
- **The dangerous direction is a release that introduces a per-tool bucket.**
  Servers with large schemas would fail it on day one, silently, with the
  description intact and the schema truncated. That is the specific regression
  worth re-running this probe for.
- **Not probed:** whether the same cap applies to Claude Code's own built-in tool
  descriptions; whether a total budget exists above 348 KB; whether *any* other
  MCP client behaves this way.
- **The 348 KB result establishes no cap at 348 KB** — not that no cap exists
  anywhere above it.
