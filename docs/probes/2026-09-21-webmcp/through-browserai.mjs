// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The same WebMCP measurement as `probe.mjs`, taken END TO END through the
// PUBLISHED BrowserAI server rather than against the child directly.
//
// Why both: probe.mjs establishes that a page can add tools to the CHILD's
// `tools/list` and put its own text into a tool result. This establishes what a
// CALLER of BrowserAI gets, which is a different question and has a different
// answer for each half -- BrowserAI answers `tools/list` from the run's own
// child, which never navigates, and forwards `tools/call` results verbatim.
// Reasoning either half from the other is exactly what this rig exists to stop.
//
//   node docs/probes/2026-09-21-webmcp/through-browserai.mjs <serverExe> <sessionDir>
//
// The three numbers to read are printed as `TOOLS BEFORE`, `TOOLS AFTER` and
// whether the page's own text appears under `snapshot`.
//
// WARNING It creates a real session under the app root's index and DESTROYS it
// at the end. Point <sessionDir> inside .work/ and nowhere else. It does not
// set BROWSERAI_ROOT, because setting it does not isolate a run (CLAUDE.md).

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { resolve } from 'node:path';

const serverExe = resolve(process.argv[2]);
const sessionDir = resolve(process.argv[3]);

// The same page as probe.mjs, so the two logs are comparable line for line.
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

const site = createServer((request, reply) => {
  reply.writeHead(200, { 'content-type': 'text/html' });
  reply.end(pageHtml);
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

const call = (name, args) => request('tools/call', { name, arguments: args });
const why = 'Measuring whether a page that registers WebMCP tools can reach a caller of BrowserAI.';
const names = (message) => (message.result?.tools ?? []).map((tool) => tool.name);

const before = await request('tools/list', {});
console.log(`\n===== TOOLS BEFORE: ${names(before).length} =====`);
console.log(names(before).filter((name) => name.includes('webmcp')).join('\n') || '(no name contains "webmcp")');

show('init', await call('browserai_init', {
  directory: sessionDir,
  purpose: 'One-shot rig for the 2026-09-21 WebMCP measurement; destroyed at the end of the run.',
}));

const named = (args) => ({ session: sessionDir, why, ...args });

show('navigate', await call('browser_navigate', named({ url: pageUrl })));
show('snapshot', await call('browser_snapshot', named({})));

const after = await request('tools/list', {});
console.log(`\n===== TOOLS AFTER: ${names(after).length} =====`);
console.log(names(after).filter((name) => !names(before).includes(name)).join('\n') || '(nothing was added)');

// And the door: a name the page put on the child's list has no verdict row, so
// BrowserAI refuses it without starting anything. Asserted by CALLING it,
// because "deny by default" is a claim about what happens rather than about
// what a file says.
show('webmcp_probe_tool_alpha (page-supplied name)', await call('webmcp_probe_tool_alpha', named({})));

show('destroy', await call('browserai_destroy', { directory: sessionDir, why }));

child.stdin.end();
await new Promise((done) => child.on('exit', done));
site.close();
console.log(`\n===== stderr =====\n${stderr}`);
