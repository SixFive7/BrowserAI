// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The same pointer measurement as `probe.mjs`, taken END TO END through the
// PUBLISHED BrowserAI server rather than against the child directly.
//
// Why both: probe.mjs establishes what upstream does under each value of
// `filePaths`; this establishes that BrowserAI's generated config actually
// reaches a caller's tool results. A pointer that is absolute in probe.mjs and
// relative here would mean the generator wrote a key the product then lost.
//
//   node docs/probes/2026-09-17-file-paths/through-browserai.mjs <serverExe> <sessionDir>
//
// ⚠️ It creates a real session under the app root's index and DESTROYS it at the
// end. Point <sessionDir> inside .work/ and nowhere else.

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { resolve } from 'node:path';

const serverExe = resolve(process.argv[2]);
const sessionDir = resolve(process.argv[3]);

const downloadBody = Buffer.from('browserai pointer probe download\n');
const pngBody = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64');
const pageHtml = [
  '<!doctype html><title>file-paths probe</title>',
  '<h1>file-paths probe</h1>',
  '<img id="i" src="/pixel.png" alt="pixel">',
  '<a id="d" download="probe-download.txt" href="/probe-download.txt">download</a>',
  '<script>console.log("probe console line"); console.error("probe console error");</script>',
].join('\n');

const site = createServer((request, reply) => {
  if (request.url === '/pixel.png') {
    reply.writeHead(200, { 'content-type': 'image/png' });
    reply.end(pngBody);
  } else if (request.url === '/probe-download.txt') {
    reply.writeHead(200, { 'content-type': 'text/plain', 'content-disposition': 'attachment' });
    reply.end(downloadBody);
  } else {
    reply.writeHead(200, { 'content-type': 'text/html' });
    reply.end(pageHtml);
  }
});
await new Promise((listening) => site.listen(0, '127.0.0.1', listening));
const pageUrl = `http://127.0.0.1:${site.address().port}/`;

const child = spawn(serverExe, [], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });

const waiting = new Map();
let pending = '';
let stderr = '';
let nextId = 1;

child.stdout.setEncoding('utf8');
child.stderr.setEncoding('utf8');
child.stderr.on('data', (chunk) => (stderr += chunk));
child.stdout.on('data', (chunk) => {
  pending += chunk;
  for (let end = pending.indexOf('\n'); end >= 0; end = pending.indexOf('\n')) {
    const line = pending.slice(0, end).trim();
    pending = pending.slice(end + 1);
    if (line.length === 0) {
      continue;
    }
    const message = JSON.parse(line);
    if (message.id !== undefined && waiting.has(message.id)) {
      waiting.get(message.id)(message);
      waiting.delete(message.id);
    }
  }
});

function request(method, params) {
  const id = nextId++;
  return new Promise((succeed, fail) => {
    const guard = setTimeout(() => fail(new Error(`${method} timed out. stderr: ${stderr}`)), 300_000);
    waiting.set(id, (message) => {
      clearTimeout(guard);
      succeed(message);
    });
    child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
  });
}

function show(label, message) {
  console.log(`\n===== ${label} =====`);
  if (message.error) {
    console.log(`ERROR ${JSON.stringify(message.error)}`);
    return message;
  }
  for (const block of message.result.content ?? []) {
    if (block.type === 'text') {
      console.log(block.text);
    } else {
      console.log(`[${block.type}] ${block.mimeType ?? ''} ${block.data ? `${block.data.length} base64 chars` : ''}`);
    }
  }
  return message;
}

function textOf(message) {
  return (message.result?.content ?? [])
    .filter((block) => block.type === 'text')
    .map((block) => block.text)
    .join('\n');
}

await request('initialize', {
  protocolVersion: '2025-06-18',
  capabilities: {},
  clientInfo: { name: 'file-paths-probe', version: '1' },
});
child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' })}\n`);

const call = (name, args) => request('tools/call', { name, arguments: args });
const why = 'Measuring which artifact pointers in a tool result are absolute after adopting filePaths.';

show('init', await call('browserai_init', {
  directory: sessionDir,
  purpose: 'One-shot rig for the 2026-09-17 filePaths pointer measurement; destroyed at the end of the run.',
  tracing: true,
}));

const named = (args) => ({ session: sessionDir, why, ...args });

show('get_config', await call('browser_get_config', named({})));
show('start_tracing', await call('browser_start_tracing', named({})));
show('navigate', await call('browser_navigate', named({ url: pageUrl })));
show('snapshot', await call('browser_snapshot', named({})));
show('screenshot (inline)', await call('browser_take_screenshot', named({})));
show('screenshot (filename)', await call('browser_take_screenshot', named({ filename: 'probe-shot.png' })));
show('pdf_save', await call('browser_pdf_save', named({ filename: 'probe.pdf' })));
show('console_messages (filename)', await call('browser_console_messages', named({ filename: 'probe-console.log' })));
show('download (click)', await call('browser_click', named({ element: 'download link', target: '#d' })));

const listed = show('network_requests', await call('browser_network_requests', named({ static: true })));
const pixelLine = textOf(listed).split('\n').find((line) => line.includes('pixel.png')) ?? '';
const pixelIndex = Number.parseInt(pixelLine.trim(), 10);
show(
  `network_request (binary response body, index ${pixelIndex})`,
  await call('browser_network_request', named({ index: pixelIndex, part: 'response-body' })));

show('storage_state', await call('browser_storage_state', named({ filename: 'probe-storage.json' })));
show('stop_tracing', await call('browser_stop_tracing', named({})));
show('destroy', await call('browserai_destroy', { directory: sessionDir, why }));

child.stdin.end();
await new Promise((done) => child.on('exit', done));
site.close();
console.log(`\n===== stderr =====\n${stderr}`);
