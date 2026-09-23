// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// What a page that registers WebMCP tools puts into a tool RESULT, and what
// `webmcp: false` takes back out, measured against the resolved child and not
// read off the bundle.
//
// @playwright/mcp 0.0.82 moved `browser_webmcp_list` and `browser_webmcp_call`
// to `skillOnly`, so neither is on the wire any more -- and in the same release
// the page's own tool NAMES, DESCRIPTIONS and inputSchemas started being
// prepended to the snapshot that every snapshot-bearing tool result carries.
// Those two are easy to read as one change and they point in opposite
// directions, which is why this measures the second instead of inferring it
// from the first.
//
// It decides nothing. The classification is in the kb entry, taken from what
// this printed.
//
//   node docs/probes/2026-09-21-webmcp/probe.mjs <payloadRoot> <scratchRoot> <on|off>
//
// `on` leaves upstream's default in force and writes no `webmcp` key at all,
// which is the state BrowserAI ships today. `off` writes `webmcp: false`. RUN
// IT TWICE AND DIFF: a line present in both is a line the key does not reach.

import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { join, resolve } from 'node:path';

const payloadRoot = resolve(process.argv[2]);
const scratchRoot = resolve(process.argv[3]);
const mode = process.argv[4] ?? 'on';

const nodeExe = join(payloadRoot, 'node', 'node.exe');
const cli = join(payloadRoot, 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js');

const sessionRoot = join(scratchRoot, mode);
const outputDir = join(sessionRoot, 'output');
mkdirSync(outputDir, { recursive: true });

// A loopback HTTP server and not a data: URL, for the same reason as the
// 2026-09-17 rig: the child blocks `file:` unless allowUnrestrictedFileAccess
// is on, which BrowserAI writes false.
//
// THE PAGE'S CONTRACT IS `document.modelContext ?? navigator.modelContext` WITH
// A `getTools()`, read out of `collectToolsInPage` in the resolved bundle. The
// descriptions below are deliberately loud: the question is whether page-
// authored text reaches a model, so text nobody could mistake for Playwright's
// own is what makes the answer readable at a glance.
const pageHtml = [
  '<!doctype html><title>webmcp probe</title>',
  '<h1>webmcp probe</h1>',
  '<p id="p">a paragraph</p>',
  '<script>',
  'document.modelContext = {',
  '  getTools: () => ([',
  '    {',
  '      name: "probe_tool_alpha",',
  '      title: "Alpha",',
  '      description: "PAGE-AUTHORED-DESCRIPTION-ALPHA and here is a sentence a page should not be able to put in front of a model",',
  '      inputSchema: { type: "object", properties: { who: { type: "string", description: "PAGE-AUTHORED-PARAM-DESCRIPTION" } } },',
  '      annotations: { readOnlyHint: true }',
  '    },',
  '    {',
  '      name: "probe_tool_beta",',
  '      description: "PAGE-AUTHORED-DESCRIPTION-BETA",',
  '      inputSchema: { type: "object", properties: {} },',
  '      annotations: { consequentialHint: true }',
  '    }',
  '  ]),',
  '  invokeTool: (name, input) => ({ called: name, input })',
  '};',
  '</script>',
].join('\n');

const server = createServer((request, reply) => {
  reply.writeHead(200, { 'content-type': 'text/html' });
  reply.end(pageHtml);
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
  filePaths: 'absolute',
  // BrowserAI's own granted set, read off BrowserConfiguration.GrantedCapabilities
  // and not invented.
  capabilities: ['config', 'vision', 'devtools', 'storage', 'network', 'pdf', 'testing'],
  ...(mode === 'off' ? { webmcp: false } : {}),
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
    // Notifications are the other half of the question: `listChanged: true` is
    // new in 0.0.82, and a tool list that changes because a PAGE changed is the
    // thing to see and not to assume.
    if (message.id === undefined) {
      console.log(`\n>>> NOTIFICATION ${message.method} ${JSON.stringify(message.params ?? {})}`);
      continue;
    }
    if (waiting.has(message.id)) {
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
      console.log(`[${block.type}] ${block.mimeType ?? ''}`);
    }
  }
  return message;
}

const initialize = await request('initialize', {
  protocolVersion: '2025-06-18',
  capabilities: {},
  clientInfo: { name: 'webmcp-probe', version: '1' },
});
console.log(`===== initialize.capabilities =====\n${JSON.stringify(initialize.result.capabilities)}`);
child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' })}\n`);

const call = (name, args) => request('tools/call', { name, arguments: args ?? {} });
const names = (message) => (message.result?.tools ?? []).map((tool) => tool.name);

const before = await request('tools/list', {});
console.log(`\n===== tools/list BEFORE any page (${names(before).length}) =====`);
console.log(names(before).filter((name) => name.includes('webmcp')).join('\n') || '(no name contains "webmcp")');

show('navigate', await call('browser_navigate', { url: pageUrl }));
show('snapshot', await call('browser_snapshot'));
show('click (a snapshot-bearing action tool)', await call('browser_click', { element: 'heading', target: 'h1' }));

const after = await request('tools/list', {});
console.log(`\n===== tools/list AFTER the page (${names(after).length}) =====`);
console.log(names(after).filter((name) => !names(before).includes(name)).join('\n') || '(nothing was added)');

show('close', await call('browser_close'));

child.stdin.end();
await new Promise((done) => child.on('exit', done));
server.close();
console.log(`\n===== stderr =====\n${stderr}`);
