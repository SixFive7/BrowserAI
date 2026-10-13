// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The local model stub of the 127 c measurement: an Anthropic Messages API on
// 127.0.0.1 that answers Claude Code's turns from a fixed script, so no credential is
// used and no inference happens. Taken from docs/probes/2026-09-24-q261/apistub.js,
// whose script and request log it keeps; the swap and wait steps of that rig are left
// out, and two things are added: it writes its own pid when it listens, and it ends
// when STUB_STOP (a file) appears, so the run script never has to stop a process.
//
// Environment: STUB_PORT; STUB_LOGDIR and STUB_TAG name its two logs; STUB_SCRIPT is
// steps joined by "||", each "tool:<name>:<json arguments>" or "text:<message>";
// STUB_STOP.

const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = Number(process.env.STUB_PORT || 8787);
const LOGDIR = process.env.STUB_LOGDIR || '.';
const TAG = process.env.STUB_TAG || 'stub';
const SCRIPT = (process.env.STUB_SCRIPT || '').split('||').filter(Boolean);
const STOP = process.env.STUB_STOP || '';

fs.mkdirSync(LOGDIR, { recursive: true });

const REQLOG = path.join(LOGDIR, `${TAG}.requests.jsonl`);
const EVLOG = path.join(LOGDIR, `${TAG}.events.log`);

function ev(line) {
  fs.appendFileSync(EVLOG, `${new Date().toISOString()} ${line}\n`);
}

let turn = 0;

function sse(res, event, data) {
  res.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`);
}

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

  req.on('data', (chunk) => {
    body += chunk;
  });

  req.on('end', () => {
    const url = req.url.split('?')[0];

    ev(`REQ ${req.method} ${req.url} bytes=${body.length}`);

    if (url.endsWith('/count_tokens')) {
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ input_tokens: 100 }));
      return;
    }

    if (!url.includes('/v1/messages')) {
      if (req.method === 'HEAD' || url === '/api/hello') {
        res.writeHead(200, { 'content-type': 'application/json' });
        res.end('{}');
        return;
      }

      res.writeHead(404, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ type: 'error', error: { type: 'not_found_error', message: `stub has no ${url}` } }));
      return;
    }

    const idx = turn++;
    let parsed;

    try {
      parsed = JSON.parse(body);
    } catch (failure) {
      parsed = body;
    }

    fs.appendFileSync(REQLOG, JSON.stringify({ turn: idx, at: new Date().toISOString(), url: req.url, body: parsed }) + '\n');

    const step = SCRIPT[idx];

    res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache', connection: 'keep-alive' });

    if (!step) {
      ev(`turn ${idx}: no script step, ending the turn`);
      emitText(res, idx, `stub: no script step for turn ${idx}`);
    } else if (step.startsWith('tool:')) {
      const rest = step.slice(5);
      const colon = rest.indexOf(':');
      const name = rest.slice(0, colon);
      const args = JSON.parse(rest.slice(colon + 1));

      ev(`turn ${idx}: emitting tool_use ${name} ${JSON.stringify(args)}`);
      emitToolUse(res, idx, name, args);
    } else {
      ev(`turn ${idx}: emitting text`);
      emitText(res, idx, step.slice(5));
    }

    res.end();
  });
});

server.listen(PORT, '127.0.0.1', () => ev(`LISTENING on 127.0.0.1:${PORT} pid=${process.pid} script=${JSON.stringify(SCRIPT)}`));

setInterval(() => {
  if (STOP && fs.existsSync(STOP)) {
    ev('STOPPING');
    process.exit(0);
  }
}, 200);
