// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Minimal OpenAI Responses-API stub, shaped exactly like codex's own
// sdk/typescript/tests/responsesProxy.ts, so the real Codex CLI can be driven with no credentials.
const http = require('http');
const fs = require('fs');
const path = require('path');
const PORT = Number(process.env.STUB_PORT || 8899);
const LOGDIR = process.env.STUB_LOGDIR || '.';
const TAG = process.env.STUB_TAG || 'cstub';
const SCRIPT = (process.env.STUB_SCRIPT || '').split('||').filter(Boolean);
const WAITFILE = process.env.STUB_WAITFILE || '';
const WAITBEFORE = Number(process.env.STUB_WAITBEFORE || -1);
fs.mkdirSync(LOGDIR, { recursive: true });
const REQLOG = path.join(LOGDIR, TAG + '.requests.jsonl');
const EVLOG = path.join(LOGDIR, TAG + '.events.log');
const ev = (s) => fs.appendFileSync(EVLOG, new Date().toISOString() + ' ' + s + '\n');
let turn = 0;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
async function waitFor(file, maxMs) {
  const t0 = Date.now();
  while (Date.now() - t0 < maxMs) { if (fs.existsSync(file)) { ev('WAITFILE present after ' + (Date.now() - t0) + 'ms'); return; } await sleep(100); }
  ev('WAITFILE TIMEOUT ' + maxMs + 'ms');
}
function write(res, e) { res.write('event: ' + e.type + '\ndata: ' + JSON.stringify(e) + '\n\n'); }
const started = (id) => ({ type: 'response.created', response: { id } });
const completed = (id) => ({ type: 'response.completed', response: { id, usage: { input_tokens: 42, input_tokens_details: { cached_tokens: 12 }, output_tokens: 5, output_tokens_details: null, total_tokens: 47 } } });
const message = (text, i) => ({ type: 'response.output_item.done', item: { type: 'message', role: 'assistant', id: 'msg_' + i, content: [{ type: 'output_text', text }] } });
const fnCall = (name, args, i) => { const ns = name.includes('/') ? name.split('/')[0] : null; const fn = name.includes('/') ? name.split('/')[1] : name; const item = { type: 'function_call', call_id: 'call_' + i, id: 'fc_' + i, name: fn, arguments: args }; if (ns) item.namespace = ns; return { type: 'response.output_item.done', item }; };


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
    ev('REQ ' + req.method + ' ' + req.url + ' bytes=' + body.length);
    if (!url.endsWith('/responses')) { res.statusCode = 404; res.end('{}'); return; }
    const idx = turn++;
    let parsed; try { parsed = JSON.parse(body); } catch (e) { parsed = body; }
    fs.appendFileSync(REQLOG, JSON.stringify({ turn: idx, at: new Date().toISOString(), url: req.url, body: parsed }) + '\n');
    if (WAITBEFORE === idx && WAITFILE) await waitFor(WAITFILE, 30000);
    await maybeSwap(idx);
    const step = SCRIPT[idx];
    res.statusCode = 200;
    res.setHeader('content-type', 'text/event-stream');
    const rid = 'resp_' + idx;
    write(res, started(rid));
    if (step && step.startsWith('tool:')) {
      const rest = step.slice(5);
      const c = rest.indexOf(':');
      ev('turn ' + idx + ': function_call ' + rest.slice(0, c) + ' ' + rest.slice(c + 1));
      write(res, fnCall(rest.slice(0, c), rest.slice(c + 1), idx));
    } else {
      const text = step ? step.slice(5) : 'stub: no script step for turn ' + idx;
      ev('turn ' + idx + ': message');
      write(res, message(text, idx));
    }
    write(res, completed(rid));
    res.end();
  });
});
server.listen(PORT, '127.0.0.1', () => { ev('LISTENING ' + PORT + ' script=' + JSON.stringify(SCRIPT)); console.log('responses stub on ' + PORT); });
