// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// OpenAI Responses API stub for the client-exit measurements (derived from
// docs/evidence/2026-09-23-client-reconnect/rig/openaistub.js, copied, not edited there).
// STATELESS: a request whose input carries no function_call_output and whose tools carry
// an mcp__probe namespace gets a function_call to mcp__probe/ping; anything else gets
// the message "done".
const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const PORT = Number(process.env.STUB_PORT || 8899);
const LOG = process.env.STUB_LOG || path.join(__dirname, 'openai-stub.log');
const ev = (s) => fs.appendFileSync(LOG, `${new Date().toISOString()} ${s}\n`);
let n = 0;

function write(res, e) { res.write('event: ' + e.type + '\ndata: ' + JSON.stringify(e) + '\n\n'); }

const server = http.createServer((req, res) => {
  let raw = '';
  req.on('data', (c) => { raw += c; });
  req.on('end', () => {
    const url = req.url.split('?')[0];
    if (!url.endsWith('/responses')) { ev(`REQ ${req.method} ${req.url} -> 404`); res.statusCode = 404; res.end('{}'); return; }
    let body = {};
    try { body = JSON.parse(raw); } catch (e) { body = {}; }
    const input = Array.isArray(body.input) ? body.input : [];
    const answered = input.some((i) => i && i.type === 'function_call_output');
    const tools = Array.isArray(body.tools) ? body.tools : [];
    const ns = tools.find((t) => t && t.type === 'namespace' && typeof t.name === 'string' && t.name.startsWith('mcp__probe'));
    const id = `${process.pid}_${++n}_${crypto.randomBytes(4).toString('hex')}`;
    const kind = ns && !answered ? 'tool' : 'text';
    ev(`REQ ${req.method} ${req.url} model=${body.model} tools=${tools.length} ns=${ns ? ns.name : null} answered=${answered} -> ${kind} id=${id}`);
    res.statusCode = 200;
    res.setHeader('content-type', 'text/event-stream');
    const rid = 'resp_' + id;
    write(res, { type: 'response.created', response: { id: rid } });
    if (kind === 'tool') {
      write(res, { type: 'response.output_item.done', item: { type: 'function_call', call_id: 'call_' + id, id: 'fc_' + id, name: 'ping', namespace: ns.name, arguments: '{}' } });
    } else {
      write(res, { type: 'response.output_item.done', item: { type: 'message', role: 'assistant', id: 'msg_' + id, content: [{ type: 'output_text', text: 'done' }] } });
    }
    write(res, { type: 'response.completed', response: { id: rid, usage: { input_tokens: 42, input_tokens_details: { cached_tokens: 12 }, output_tokens: 5, output_tokens_details: null, total_tokens: 47 } } });
    res.end();
  });
});
server.listen(PORT, '127.0.0.1', () => ev(`LISTENING 127.0.0.1:${PORT} pid=${process.pid}`));
