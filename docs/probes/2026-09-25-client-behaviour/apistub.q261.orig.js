// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Minimal Anthropic Messages API stub so the real Claude Code CLI can be driven
// deterministically with no credentials. Scripts a fixed sequence of tool calls.
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = Number(process.env.STUB_PORT || 8787);
const LOGDIR = process.env.STUB_LOGDIR || '.';
const TAG = process.env.STUB_TAG || 'stub';
// Script: comma-separated steps. "tool:<name>:<jsonargs>" or "text:<msg>"
const SCRIPT = (process.env.STUB_SCRIPT || '').split('||').filter(Boolean);
// Before serving request index N (0-based), wait for this file to exist.
const WAITFILE = process.env.STUB_WAITFILE || '';
const WAITBEFORE = Number(process.env.STUB_WAITBEFORE || -1);

fs.mkdirSync(LOGDIR, { recursive: true });
const REQLOG = path.join(LOGDIR, `${TAG}.requests.jsonl`);
const EVLOG = path.join(LOGDIR, `${TAG}.events.log`);
function ev(s) { fs.appendFileSync(EVLOG, `${new Date().toISOString()} ${s}\n`); }

let turn = 0;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function waitFor(file, maxMs) {
  const t0 = Date.now();
  while (Date.now() - t0 < maxMs) {
    if (fs.existsSync(file)) { ev(`WAITFILE present after ${Date.now() - t0}ms: ${file}`); return true; }
    await sleep(100);
  }
  ev(`WAITFILE TIMEOUT after ${maxMs}ms: ${file}`);
  return false;
}

function sse(res, event, data) {
  res.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`);
}

function emitToolUse(res, id, name, args) {
  sse(res, 'message_start', { type: 'message_start', message: { id: `msg_${id}`, type: 'message', role: 'assistant', model: 'claude-sonnet-5', content: [], stop_reason: null, stop_sequence: null, usage: { input_tokens: 10, output_tokens: 1 } } });
  sse(res, 'content_block_start', { type: 'content_block_start', index: 0, content_block: { type: 'tool_use', id: `toolu_${id}`, name, input: {} } });
  sse(res, 'content_block_delta', { type: 'content_block_delta', index: 0, delta: { type: 'input_json_delta', partial_json: JSON.stringify(args) } });
  sse(res, 'content_block_stop', { type: 'content_block_stop', index: 0 });
  sse(res, 'message_delta', { type: 'message_delta', delta: { stop_reason: 'tool_use', stop_sequence: null }, usage: { output_tokens: 20 } });
  sse(res, 'message_stop', { type: 'message_stop' });
}

function emitText(res, id, text) {
  sse(res, 'message_start', { type: 'message_start', message: { id: `msg_${id}`, type: 'message', role: 'assistant', model: 'claude-sonnet-5', content: [], stop_reason: null, stop_sequence: null, usage: { input_tokens: 10, output_tokens: 1 } } });
  sse(res, 'content_block_start', { type: 'content_block_start', index: 0, content_block: { type: 'text', text: '' } });
  sse(res, 'content_block_delta', { type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text } });
  sse(res, 'content_block_stop', { type: 'content_block_stop', index: 0 });
  sse(res, 'message_delta', { type: 'message_delta', delta: { stop_reason: 'end_turn', stop_sequence: null }, usage: { output_tokens: 20 } });
  sse(res, 'message_stop', { type: 'message_stop' });
}


async function maybeSwap(idx) {
  const SB = process.env.STUB_SWAP_BEFORE;
  if (SB === undefined || Number(SB) !== idx) return;
  const req = process.env.STUB_SWAP_REQUEST, done = process.env.STUB_APPLY_DONE, ver = process.env.STUB_VERSION_FILE;
  const waitMs = Number(process.env.STUB_SWAP_WAIT_MS || 4000);
  ev('SWAP: requesting the swap before turn ' + idx);
  if (req) fs.writeFileSync(req, 'now');
  await sleep(1200);
  ev('SWAP: running the apply (VERSION -> 2.0.0)');
  if (ver) fs.writeFileSync(ver, '2.0.0');
  if (done) fs.writeFileSync(done, 'done');
  const swapDone = process.env.STUB_SWAP_DONE;
  if (swapDone) {
    const t0 = Date.now();
    while (Date.now() - t0 < waitMs) { if (fs.existsSync(swapDone)) { ev('SWAP: relay reported the swap complete after ' + (Date.now() - t0) + 'ms'); break; } await sleep(50); }
    if (!fs.existsSync(swapDone)) ev('SWAP: TIMED OUT waiting for the relay to report the swap complete');
  } else {
    await sleep(waitMs);
  }
  ev('SWAP: serving turn ' + idx);
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
    fs.appendFileSync(REQLOG, JSON.stringify({ turn: idx, at: new Date().toISOString(), url: req.url, body: (() => { try { return JSON.parse(body); } catch (e) { return body; } })() }) + '\n');
    if (WAITBEFORE === idx && WAITFILE) await waitFor(WAITFILE, 30000);
    await maybeSwap(idx);
    const step = SCRIPT[idx];
    res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache', connection: 'keep-alive' });
    if (!step) { ev(`turn ${idx}: no script step, ending turn`); emitText(res, idx, `stub: no script step for turn ${idx}`); res.end(); return; }
    if (step.startsWith('tool:')) {
      const rest = step.slice(5);
      const c = rest.indexOf(':');
      const name = rest.slice(0, c);
      const args = JSON.parse(rest.slice(c + 1));
      ev(`turn ${idx}: emitting tool_use ${name} ${JSON.stringify(args)}`);
      emitToolUse(res, idx, name, args);
    } else {
      ev(`turn ${idx}: emitting text`);
      emitText(res, idx, step.slice(5));
    }
    res.end();
  });
});
server.listen(PORT, '127.0.0.1', () => { ev(`LISTENING on 127.0.0.1:${PORT} script=${JSON.stringify(SCRIPT)}`); console.log(`stub listening ${PORT}`); });
