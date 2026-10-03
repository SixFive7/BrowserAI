// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Anthropic Messages API stub for driving the real Claude Code with no credential.
// Derived from docs/probes/2026-09-24-q261/apistub.js (sha256 33e1dc3b...), with one
// change: the script is a JSON file of steps, and each step may wait for a file
// and/or a delay before it is served.
//   STUB_PORT, STUB_LOGDIR, STUB_TAG, STUB_SCRIPT_FILE
//   step: { "waitFile": "<abs path>", "waitMaxMs": 60000, "delayMs": 0, "tool": "<name>", "args": {...} }
//      or { ..., "text": "<assistant text>" }
'use strict';
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = Number(process.env.STUB_PORT || 8787);
const LOGDIR = process.env.STUB_LOGDIR || '.';
const TAG = process.env.STUB_TAG || 'stub';
const SCRIPT = JSON.parse(fs.readFileSync(process.env.STUB_SCRIPT_FILE, 'utf8'));

fs.mkdirSync(LOGDIR, { recursive: true });
const REQLOG = path.join(LOGDIR, `${TAG}.requests.jsonl`);
const EVLOG = path.join(LOGDIR, `${TAG}.events.log`);
const ev = (s) => fs.appendFileSync(EVLOG, `${new Date().toISOString()} ${s}\n`);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let turn = 0;

async function waitFor(file, maxMs) {
  const t0 = Date.now();
  while (Date.now() - t0 < maxMs) {
    if (fs.existsSync(file)) { ev(`WAITFILE present after ${Date.now() - t0}ms: ${file}`); return true; }
    await sleep(100);
  }
  ev(`WAITFILE TIMEOUT after ${maxMs}ms: ${file}`);
  return false;
}

function sse(res, event, data) { res.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`); }

function start(res, id) {
  sse(res, 'message_start', { type: 'message_start', message: { id: `msg_${id}`, type: 'message', role: 'assistant', model: 'claude-sonnet-5', content: [], stop_reason: null, stop_sequence: null, usage: { input_tokens: 10, output_tokens: 1 } } });
}

function emitToolUse(res, id, name, args) {
  start(res, id);
  sse(res, 'content_block_start', { type: 'content_block_start', index: 0, content_block: { type: 'tool_use', id: `toolu_${id}`, name, input: {} } });
  sse(res, 'content_block_delta', { type: 'content_block_delta', index: 0, delta: { type: 'input_json_delta', partial_json: JSON.stringify(args) } });
  sse(res, 'content_block_stop', { type: 'content_block_stop', index: 0 });
  sse(res, 'message_delta', { type: 'message_delta', delta: { stop_reason: 'tool_use', stop_sequence: null }, usage: { output_tokens: 20 } });
  sse(res, 'message_stop', { type: 'message_stop' });
}

function emitText(res, id, text) {
  start(res, id);
  sse(res, 'content_block_start', { type: 'content_block_start', index: 0, content_block: { type: 'text', text: '' } });
  sse(res, 'content_block_delta', { type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text } });
  sse(res, 'content_block_stop', { type: 'content_block_stop', index: 0 });
  sse(res, 'message_delta', { type: 'message_delta', delta: { stop_reason: 'end_turn', stop_sequence: null }, usage: { output_tokens: 20 } });
  sse(res, 'message_stop', { type: 'message_stop' });
}

const server = http.createServer((req, res) => {
  let body = '';
  req.on('data', (c) => { body += c; });
  req.on('end', async () => {
    const url = req.url.split('?')[0];
    ev(`REQ ${req.method} ${req.url} bytes=${body.length}`);
    if (url.endsWith('/count_tokens')) {
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ input_tokens: 100 }));
      return;
    }
    if (!url.includes('/v1/messages')) {
      if (req.method === 'HEAD' || url === '/api/hello') { res.writeHead(200, { 'content-type': 'application/json' }); res.end('{}'); return; }
      res.writeHead(404, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ type: 'error', error: { type: 'not_found_error', message: `stub has no ${url}` } }));
      return;
    }
    const idx = turn++;
    let parsed; try { parsed = JSON.parse(body); } catch (e) { parsed = body; }
    const toolNames = parsed && Array.isArray(parsed.tools) ? parsed.tools.map((t) => t.name) : null;
    ev(`turn ${idx}: model=${parsed && parsed.model} tools=${toolNames ? toolNames.length : 'none'} ${JSON.stringify((toolNames || []).filter((n) => /browserai|probe/i.test(n)))}`);
    fs.appendFileSync(REQLOG, JSON.stringify({ turn: idx, at: new Date().toISOString(), url: req.url, body: parsed }) + '\n');
    res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache', connection: 'keep-alive' });
    const step = SCRIPT[idx];
    if (step && step.waitFile) await waitFor(step.waitFile, step.waitMaxMs || 60000);
    if (step && step.delayMs) { ev(`turn ${idx}: delaying ${step.delayMs} ms`); await sleep(step.delayMs); }
    if (!step) { ev(`turn ${idx}: no script step, ending turn`); emitText(res, idx, `stub: no script step for turn ${idx}`); res.end(); return; }
    if (step.tool) { ev(`turn ${idx}: emitting tool_use ${step.tool} ${JSON.stringify(step.args || {})}`); emitToolUse(res, `${TAG}_${idx}`.replace(/[^A-Za-z0-9_]/g, "_"), step.tool, step.args || {}); }
    else { ev(`turn ${idx}: emitting text`); emitText(res, `${TAG}_${idx}`.replace(/[^A-Za-z0-9_]/g, '_'), step.text || ''); }
    res.end();
  });
});
server.listen(PORT, '127.0.0.1', () => { ev(`LISTENING on 127.0.0.1:${PORT} steps=${SCRIPT.length}`); console.log(`stub listening ${PORT}`); });
