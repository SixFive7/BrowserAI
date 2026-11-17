// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Captures the request body Claude Code sends to the Anthropic API, then answers
// with a minimal valid SSE stream so the client exits cleanly.
// Authorization / api-key headers are NEVER written to disk.
const http = require('http');
const fs = require('fs');
const path = require('path');

const outDir = process.argv[2];
const port = Number(process.argv[3] || 8787);
fs.mkdirSync(outDir, { recursive: true });

let n = 0;

const server = http.createServer((req, res) => {
  const chunks = [];
  req.on('data', (c) => chunks.push(c));
  req.on('end', () => {
    const body = Buffer.concat(chunks);
    n += 1;
    const safeHeaders = {};
    for (const [k, v] of Object.entries(req.headers)) {
      const lk = k.toLowerCase();
      safeHeaders[k] = (lk === 'authorization' || lk === 'x-api-key' || lk === 'cookie') ? '<redacted>' : v;
    }
    const file = path.join(outDir, `req-${String(n).padStart(3, '0')}.json`);
    fs.writeFileSync(file, JSON.stringify({
      seq: n, method: req.method, url: req.url, headers: safeHeaders, bodyBytes: body.length,
    }, null, 2));
    if (body.length) fs.writeFileSync(path.join(outDir, `body-${String(n).padStart(3, '0')}.json`), body);
    process.stderr.write(`captured ${req.method} ${req.url} ${body.length}B -> ${file}\n`);

    if (req.url.includes('count_tokens')) {
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ input_tokens: 1 }));
      return;
    }

    const isStream = body.length > 0 && body.toString('utf8').includes('"stream":true');
    if (!isStream) {
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({
        id: 'msg_probe', type: 'message', role: 'assistant', model: 'probe',
        content: [{ type: 'text', text: 'CAPTURED' }],
        stop_reason: 'end_turn', stop_sequence: null,
        usage: { input_tokens: 1, output_tokens: 1 },
      }));
      return;
    }

    res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache', connection: 'keep-alive' });
    const ev = (t, d) => res.write(`event: ${t}\ndata: ${JSON.stringify(d)}\n\n`);
    ev('message_start', { type: 'message_start', message: { id: 'msg_probe', type: 'message', role: 'assistant', model: 'probe', content: [], stop_reason: null, stop_sequence: null, usage: { input_tokens: 1, output_tokens: 1 } } });
    ev('content_block_start', { type: 'content_block_start', index: 0, content_block: { type: 'text', text: '' } });
    ev('content_block_delta', { type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text: 'CAPTURED' } });
    ev('content_block_stop', { type: 'content_block_stop', index: 0 });
    ev('message_delta', { type: 'message_delta', delta: { stop_reason: 'end_turn', stop_sequence: null }, usage: { output_tokens: 1 } });
    ev('message_stop', { type: 'message_stop' });
    res.end();
  });
});

server.listen(port, '127.0.0.1', () => process.stderr.write(`capture listening on 127.0.0.1:${port}\n`));
