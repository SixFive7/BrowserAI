// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// OpenAI Responses API stub for driving the real Codex with no credential.
// Derived from docs/evidence/2026-09-23-client-reconnect/rig/openaistub.js
// (sha256 1434fa04...), itself shaped after codex's sdk/typescript/tests/responsesProxy.ts,
// with a JSON script of steps, each of which may wait for a file and/or a delay.
//   STUB_PORT, STUB_LOGDIR, STUB_TAG, STUB_SCRIPT_FILE
//   step: { "waitFile": "...", "waitMaxMs": 60000, "delayMs": 0, "tool": "<namespace>/<name>", "args": {...} }
//      or { ..., "text": "..." }
'use strict';
const http = require('http');
const fs = require('fs');
const path = require('path');
const PORT = Number(process.env.STUB_PORT || 8899);
const LOGDIR = process.env.STUB_LOGDIR || '.';
const TAG = process.env.STUB_TAG || 'cstub';
const SCRIPT = JSON.parse(fs.readFileSync(process.env.STUB_SCRIPT_FILE, 'utf8'));
fs.mkdirSync(LOGDIR, { recursive: true });
const REQLOG = path.join(LOGDIR, TAG + '.requests.jsonl');
const EVLOG = path.join(LOGDIR, TAG + '.events.log');
const ev = (s) => fs.appendFileSync(EVLOG, new Date().toISOString() + ' ' + s + '\n');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const uid = (i) => `${TAG}_${i}`.replace(/[^A-Za-z0-9_]/g, '_');
let turn = 0;
async function waitFor(file, maxMs) {
  const t0 = Date.now();
  while (Date.now() - t0 < maxMs) { if (fs.existsSync(file)) { ev('WAITFILE present after ' + (Date.now() - t0) + 'ms ' + file); return; } await sleep(100); }
  ev('WAITFILE TIMEOUT ' + maxMs + 'ms ' + file);
}
function write(res, e) { res.write('event: ' + e.type + '\ndata: ' + JSON.stringify(e) + '\n\n'); }
const started = (id) => ({ type: 'response.created', response: { id } });
const completed = (id) => ({ type: 'response.completed', response: { id, usage: { input_tokens: 42, input_tokens_details: { cached_tokens: 12 }, output_tokens: 5, output_tokens_details: null, total_tokens: 47 } } });
const message = (text, i) => ({ type: 'response.output_item.done', item: { type: 'message', role: 'assistant', id: 'msg_' + uid(i), content: [{ type: 'output_text', text }] } });
const fnCall = (name, args, i) => {
  const ns = name.includes('/') ? name.split('/')[0] : null;
  const fn = name.includes('/') ? name.split('/')[1] : name;
  const item = { type: 'function_call', call_id: 'call_' + uid(i), id: 'fc_' + uid(i), name: fn, arguments: JSON.stringify(args || {}) };
  if (ns) item.namespace = ns;
  return { type: 'response.output_item.done', item };
};
const server = http.createServer((req, res) => {
  let body = '';
  req.on('data', (c) => { body += c; });
  req.on('end', async () => {
    const url = req.url.split('?')[0];
    ev('REQ ' + req.method + ' ' + req.url + ' bytes=' + body.length);
    if (!url.endsWith('/responses')) { res.statusCode = 404; res.end('{}'); return; }
    const idx = turn++;
    let parsed; try { parsed = JSON.parse(body); } catch (e) { parsed = body; }
    const names = parsed && Array.isArray(parsed.tools) ? parsed.tools.map((t) => (t.type === 'namespace' ? `ns:${t.name}[${(t.tools || []).map((x) => x.name).join(',')}]` : t.name)) : [];
    ev(`turn ${idx}: tools=${names.length} ${JSON.stringify(names.filter((n) => /browserai|probe/i.test(n)))}`);
    fs.appendFileSync(REQLOG, JSON.stringify({ turn: idx, at: new Date().toISOString(), url: req.url, body: parsed }) + '\n');
    const step = SCRIPT[idx];
    if (step && step.waitFile) await waitFor(step.waitFile, step.waitMaxMs || 60000);
    if (step && step.delayMs) { ev(`turn ${idx}: delaying ${step.delayMs} ms`); await sleep(step.delayMs); }
    res.statusCode = 200;
    res.setHeader('content-type', 'text/event-stream');
    const rid = 'resp_' + uid(idx);
    write(res, started(rid));
    if (step && step.tool) {
      ev('turn ' + idx + ': function_call ' + step.tool + ' ' + JSON.stringify(step.args || {}));
      write(res, fnCall(step.tool, step.args, idx));
    } else {
      const text = step ? (step.text || '') : 'stub: no script step for turn ' + idx;
      ev('turn ' + idx + ': message');
      write(res, message(text, idx));
    }
    write(res, completed(rid));
    res.end();
  });
});
server.listen(PORT, '127.0.0.1', () => { ev('LISTENING ' + PORT + ' steps=' + SCRIPT.length); console.log('responses stub on ' + PORT); });
