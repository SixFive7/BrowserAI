// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Anthropic Messages API stub for the client-exit measurements (derived from
// docs/evidence/2026-09-23-client-reconnect/rig/apistub.js, copied, not edited there).
// STATELESS: a request whose tool list carries an mcp__probe__ tool and whose last
// user message carries no tool_result gets a tool_use for mcp__probe__ping; every
// other request gets the text "done" and end_turn. Ids are unique per response.
const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const PORT = Number(process.env.STUB_PORT || 8787);
const LOG = process.env.STUB_LOG || path.join(__dirname, 'anthropic-stub.log');
const ev = (s) => fs.appendFileSync(LOG, `${new Date().toISOString()} ${s}\n`);
let n = 0;

function sse(res, event, data) { res.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`); }

// Any tool_result anywhere in the conversation means the one scripted call has been made.
// (Claude Code 2.1.288 does not always end the request with the tool_result message, so a
// last-message check loops; the smoke run of 2026-10-03 01:34Z recorded 1,500+ requests.)
function hasToolResult(body) {
  const msgs = Array.isArray(body.messages) ? body.messages : [];
  return msgs.some((m) => m && Array.isArray(m.content) && m.content.some((c) => c && c.type === 'tool_result'));
}

function probeTool(body) {
  const tools = Array.isArray(body.tools) ? body.tools : [];
  const t = tools.find((x) => x && typeof x.name === 'string' && x.name.startsWith('mcp__probe__'));
  return t ? t.name : null;
}

function respond(res, body, kind, toolName, id) {
  const model = body.model || 'claude-sonnet-5';
  const usage = { input_tokens: 10, output_tokens: 1 };
  if (body.stream === false) {
    const content = kind === 'tool'
      ? [{ type: 'tool_use', id: `toolu_${id}`, name: toolName, input: {} }]
      : [{ type: 'text', text: 'stub-turn-finished' }];
    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify({ id: `msg_${id}`, type: 'message', role: 'assistant', model, content,
      stop_reason: kind === 'tool' ? 'tool_use' : 'end_turn', stop_sequence: null, usage }));
    return;
  }
  res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache', connection: 'keep-alive' });
  sse(res, 'message_start', { type: 'message_start', message: { id: `msg_${id}`, type: 'message', role: 'assistant', model, content: [], stop_reason: null, stop_sequence: null, usage } });
  if (kind === 'tool') {
    sse(res, 'content_block_start', { type: 'content_block_start', index: 0, content_block: { type: 'tool_use', id: `toolu_${id}`, name: toolName, input: {} } });
    sse(res, 'content_block_delta', { type: 'content_block_delta', index: 0, delta: { type: 'input_json_delta', partial_json: '{}' } });
  } else {
    sse(res, 'content_block_start', { type: 'content_block_start', index: 0, content_block: { type: 'text', text: '' } });
    sse(res, 'content_block_delta', { type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text: 'stub-turn-finished' } });
  }
  sse(res, 'content_block_stop', { type: 'content_block_stop', index: 0 });
  sse(res, 'message_delta', { type: 'message_delta', delta: { stop_reason: kind === 'tool' ? 'tool_use' : 'end_turn', stop_sequence: null }, usage: { output_tokens: 5 } });
  sse(res, 'message_stop', { type: 'message_stop' });
  res.end();
}

const server = http.createServer((req, res) => {
  let raw = '';
  req.on('data', (c) => { raw += c; });
  req.on('end', () => {
    const url = req.url.split('?')[0];
    if (url.endsWith('/count_tokens')) {
      ev(`REQ ${req.method} ${req.url} -> count_tokens`);
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ input_tokens: 100 }));
      return;
    }
    if (!url.includes('/v1/messages')) {
      ev(`REQ ${req.method} ${req.url} -> 404`);
      if (req.method === 'HEAD') { res.writeHead(200); res.end(); return; }
      res.writeHead(404, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ type: 'error', error: { type: 'not_found_error', message: `stub has no ${url}` } }));
      return;
    }
    let body = {};
    try { body = JSON.parse(raw); } catch (e) { body = {}; }
    const id = `${process.pid}_${++n}_${crypto.randomBytes(4).toString('hex')}`;
    const tool = probeTool(body);
    const kind = tool && !hasToolResult(body) ? 'tool' : 'text';
    const msgs = Array.isArray(body.messages) ? body.messages : [];
    const shape = msgs.map((m) => `${m.role}:${Array.isArray(m.content) ? m.content.map((c) => c && c.type).join('+') : typeof m.content}`).join(',');
    ev(`REQ ${req.method} ${req.url} model=${body.model} stream=${body.stream} tools=${Array.isArray(body.tools) ? body.tools.length : 0} probe=${tool} msgs=[${shape.slice(0, 300)}] -> ${kind} id=${id}`);
    respond(res, body, kind, tool, id);
  });
});
server.listen(PORT, '127.0.0.1', () => ev(`LISTENING 127.0.0.1:${PORT} pid=${process.pid}`));
