// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A stand-in for BrowserAI's background, for the maintainer's 127 c measurement of
// 2026-10-13 (his 151 b). It serves the pipe a relay is pointed at with --pipe, answers
// what a relay sends it, and ends the first relay with browserai/end right after that
// relay's first tools/call, the way an update ends a relay, so that the client starts a
// second one. No process is stopped by anybody. In the control mode it first writes
// another digest into every record under the relays' data root, so the second relay
// meets a record that names a list other than its own.
//
// Environment: STANDIN_PIPE, the pipe's full name; STANDIN_LOG, a JSON-lines file;
// STANDIN_MODE, "same" or "differ"; STANDIN_DATA_ROOT, the relays' data root;
// STANDIN_STOP, a file whose appearance ends the stand-in.

const net = require('net');
const fs = require('fs');
const path = require('path');

const PIPE = process.env.STANDIN_PIPE;
const LOG = process.env.STANDIN_LOG;
const MODE = process.env.STANDIN_MODE || 'same';
const DATA_ROOT = process.env.STANDIN_DATA_ROOT || '';
const STOP = process.env.STANDIN_STOP || '';

function log(entry) {
  fs.appendFileSync(LOG, JSON.stringify({ at: new Date().toISOString(), ...entry }) + '\n');
}

let connections = 0;
let ended = false;

// The control: every record names another list from here on.
function rewriteRecords() {
  const folder = path.join(DATA_ROOT, 'tool-lists');
  const names = fs.existsSync(folder) ? fs.readdirSync(folder).filter((name) => name.endsWith('.json')) : [];

  for (const name of names) {
    const file = path.join(folder, name);
    const record = JSON.parse(fs.readFileSync(file, 'utf8'));
    const before = record.digest;

    record.digest = 'f'.repeat(64);
    fs.writeFileSync(file, JSON.stringify(record, null, 2));
    log({ event: 'record-rewritten', name, before, after: record.digest });
  }

  if (names.length === 0) {
    log({ event: 'record-rewritten', name: null, note: 'there was no record to rewrite' });
  }
}

const server = net.createServer((socket) => {
  const connection = ++connections;
  let buffered = '';
  let calls = 0;

  log({ event: 'connected', connection });

  const send = (frame) => {
    socket.write(JSON.stringify(frame) + '\n');
    log({ event: 'sent', connection, method: frame.method, id: frame.id, error: frame.error ? frame.error.message : undefined });
  };

  const on = (frame) => {
    const entry = { event: 'received', connection, method: frame.method, id: frame.id };

    if (frame.method === 'browserai/hello') {
      const greeting = frame.params || {};

      Object.assign(entry, {
        build: greeting.build,
        relayPid: greeting.relayPid,
        clientPid: greeting.clientPid,
        client: greeting.client,
        clientStarted: greeting.conversation ? greeting.conversation.clientStarted : undefined,
        dataRoot: greeting.dataRoot,
      });
    }

    if (frame.method === 'tools/call') {
      entry.tool = frame.params ? frame.params.name : undefined;
    }

    log(entry);

    if (frame.method === 'browserai/hello') {
      send({ jsonrpc: '2.0', id: frame.id, result: { build: frame.params.build, pid: process.pid } });
    } else if (frame.method === 'initialize') {
      send({
        jsonrpc: '2.0',
        id: frame.id,
        result: {
          protocolVersion: frame.params.protocolVersion,
          capabilities: { tools: {} },
          serverInfo: { name: 'BrowserAI', version: 'stand-in' },
        },
      });
    } else if (frame.method === 'ping') {
      send({ jsonrpc: '2.0', id: frame.id, result: {} });
    } else if (frame.method === 'tools/call') {
      calls++;
      send({
        jsonrpc: '2.0',
        id: frame.id,
        result: { content: [{ type: 'text', text: `stand-in answered call ${calls} on connection ${connection}` }] },
      });

      if (connection === 1 && calls === 1 && !ended) {
        ended = true;

        if (MODE === 'differ') {
          rewriteRecords();
        }

        send({ jsonrpc: '2.0', method: 'browserai/end', params: { now: true, version: '9.9.9-stand-in' } });
      }
    } else if (frame.id !== undefined && frame.method) {
      send({ jsonrpc: '2.0', id: frame.id, error: { code: -32601, message: `the stand-in does not serve ${frame.method}` } });
    }
  };

  socket.on('data', (chunk) => {
    buffered += chunk.toString('utf8');

    let newline;

    while ((newline = buffered.indexOf('\n')) >= 0) {
      const line = buffered.slice(0, newline).trim();
      buffered = buffered.slice(newline + 1);

      if (!line) {
        continue;
      }

      let frame;

      try {
        frame = JSON.parse(line);
      } catch (failure) {
        log({ event: 'unreadable', connection, bytes: line.length });
        continue;
      }

      on(frame);
    }
  });

  socket.on('close', () => log({ event: 'closed', connection }));
  socket.on('error', (failure) => log({ event: 'socket-error', connection, message: failure.message }));
});

server.on('error', (failure) => {
  log({ event: 'server-error', message: failure.message });
  process.exit(2);
});

server.listen(PIPE, () => log({ event: 'listening', pipe: PIPE, mode: MODE, pid: process.pid }));

setInterval(() => {
  if (STOP && fs.existsSync(STOP)) {
    log({ event: 'stopping' });
    process.exit(0);
  }
}, 200);
