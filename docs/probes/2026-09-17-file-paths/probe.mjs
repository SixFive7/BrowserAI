// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Which pointer shapes in a tool result become ABSOLUTE under
// `filePaths: "absolute"`, measured against the resolved child rather than read
// off the pull request that added the option.
//
// Runs the payload's own cli.js over stdio with a generated config, drives one
// page, and prints every tool result verbatim. It decides nothing: the
// classification is in the kb entry, taken from what this printed. RUN IT TWICE,
// once per mode, and diff -- a shape that reads the same in both is a shape the
// option does not reach, and a shape that is already absolute in `relative` mode
// was never one of the six.
//
//   node docs/probes/2026-09-17-file-paths/probe.mjs <payloadRoot> <scratchRoot> <relative|absolute>

import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { join, resolve } from 'node:path';

const payloadRoot = resolve(process.argv[2]);
const scratchRoot = resolve(process.argv[3]);
const mode = process.argv[4] ?? 'absolute';

const nodeExe = join(payloadRoot, 'node', 'node.exe');
const cli = join(payloadRoot, 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js');

const sessionRoot = join(scratchRoot, mode);
const outputDir = join(sessionRoot, 'output');
mkdirSync(outputDir, { recursive: true });

// A loopback HTTP server rather than a file on disk or a data: URL. The child
// blocks the `file:` protocol unless allowUnrestrictedFileAccess is on -- which
// BrowserAI writes false -- and a data: URL produces no network request at all,
// so the BINARY RESPONSE BODY shape would be unmeasurable against one.
const downloadBody = Buffer.from('probe download body\n');

// A 1x1 PNG: a real binary response body, so `browser_network_request` has to
// write a file rather than inline the bytes as text.
const pngBody = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64');

const pageHtml = [
  '<!doctype html><title>file-paths probe</title>',
  '<h1>file-paths probe</h1>',
  '<p id="p">a paragraph</p>',
  '<img id="i" src="/pixel.png" alt="pixel">',
  '<a id="d" download="probe-download.txt" href="/probe-download.txt">download</a>',
  '<script>console.log("probe console line"); console.error("probe console error");</script>',
].join('\n');

const server = createServer((request, reply) => {
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
await new Promise((listening) => server.listen(0, '127.0.0.1', listening));
const pageUrl = `http://127.0.0.1:${server.address().port}/`;

const config = {
  browser: {
    browserName: 'chromium',
    userDataDir: join(sessionRoot, 'profile'),
    launchOptions: { channel: 'chromium', headless: true, downloadsPath: join(sessionRoot, 'downloads') },
    contextOptions: { viewport: { width: 1280, height: 720 } },
  },
  outputDir,
  saveSession: true,
  filePaths: mode,
  // BrowserAI's own granted set, read off BrowserConfiguration.GrantedCapabilities
  // rather than invented. `core*` is unconditional and naming one does nothing.
  capabilities: ['config', 'vision', 'devtools', 'storage', 'network', 'pdf', 'testing'],
};

const configPath = join(sessionRoot, 'config.json');
writeFileSync(configPath, JSON.stringify(config, null, 2), 'utf8');

const environment = {};
for (const [name, value] of Object.entries(process.env)) {
  if (/^(PLAYWRIGHT_MCP|DEBUG|NODE_OPTIONS|NODE_PATH)/i.test(name)) {
    continue;
  }
  environment[name] = value;
}
environment.PLAYWRIGHT_BROWSERS_PATH = join(process.env.LOCALAPPDATA, 'BrowserAI', 'browsers');
environment.PLAYWRIGHT_SKIP_BROWSER_GC = '1';

const child = spawn(nodeExe, [cli, '--config', configPath], {
  cwd: sessionRoot,
  env: environment,
  stdio: ['pipe', 'pipe', 'pipe'],
  windowsHide: true,
});

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
    const guard = setTimeout(() => fail(new Error(`${method} timed out. stderr: ${stderr}`)), 120_000);
    waiting.set(id, (message) => {
      clearTimeout(guard);
      succeed(message);
    });
    child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
  });
}

function textOf(message) {
  return (message.result?.content ?? [])
    .filter((block) => block.type === 'text')
    .map((block) => block.text)
    .join('\n');
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

await request('initialize', {
  protocolVersion: '2025-06-18',
  capabilities: {},
  clientInfo: { name: 'file-paths-probe', version: '1' },
});
child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' })}\n`);

const call = (name, args) => request('tools/call', { name, arguments: args ?? {} });

show('get_config', await call('browser_get_config'));
show('start_tracing', await call('browser_start_tracing'));
show('navigate', await call('browser_navigate', { url: pageUrl }));
show('snapshot', await call('browser_snapshot'));
show('screenshot (inline)', await call('browser_take_screenshot'));
show('screenshot (filename)', await call('browser_take_screenshot', { filename: 'probe-shot.png' }));
show('pdf_save', await call('browser_pdf_save', { filename: 'probe.pdf' }));
show('console_messages (inline)', await call('browser_console_messages'));
show('console_messages (filename)', await call('browser_console_messages', { filename: 'probe-console.log' }));
show('download (click)', await call('browser_click', { element: 'download link', target: '#d' }));

const listed = show('network_requests', await call('browser_network_requests', { static: true }));
// The INDEX upstream printed, read off its own line, rather than the position of
// the line within the block -- the block opens with a `### Result` heading, and
// counting lines makes every index one too high.
const pixelLine = textOf(listed).split('\n').find((line) => line.includes('pixel.png')) ?? '';
const pixelIndex = Number.parseInt(pixelLine.trim(), 10);
show(
  `network_request (binary response body, index ${pixelIndex})`,
  await call('browser_network_request', { index: pixelIndex, part: 'response-body' }));

show('network_requests (filename)', await call('browser_network_requests', { static: true, filename: 'probe-network.txt' }));
show('storage_state', await call('browser_storage_state', { filename: 'probe-storage.json' }));
show('stop_tracing', await call('browser_stop_tracing'));
show('close', await call('browser_close'));

child.stdin.end();
await new Promise((done) => child.on('exit', done));
server.close();
console.log(`\n===== stderr =====\n${stderr}`);
