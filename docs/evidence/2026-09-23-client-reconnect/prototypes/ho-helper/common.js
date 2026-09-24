// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr


// Shared helpers for the handover prototype. Every write to fd 1 is writeSync so
// nothing is buffered in a process that is about to exit.
const fs = require('fs');
const path = require('path');
function mkLog(logdir, tag) {
  fs.mkdirSync(logdir, { recursive: true });
  const f = path.join(logdir, tag + '.handover.log');
  return (kind, detail) => fs.appendFileSync(f, `${new Date().toISOString()} ${process.pid} ${kind} ${detail}\n`);
}
function out(obj) { fs.writeSync(1, JSON.stringify(obj) + '\n'); }
function toolsList() {
  return [{
    name: 'ping',
    description: 'Returns pong and the identity of the server process that answered.',
    inputSchema: { type: 'object', properties: { note: { type: 'string', description: 'Optional note echoed back.' } }, additionalProperties: false },
  }];
}
module.exports = { mkLog, out, toolsList };
