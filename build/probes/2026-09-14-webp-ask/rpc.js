// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Minimal newline-delimited JSON-RPC stdio client. No product or SDK type on the
// path: JSON.parse / JSON.stringify only. Modelled on
// tests/BrowserAI.Tests/Harness/RawStdioClient.cs and .work/2026-08-27-sandbox-confirm/drive.js
// (read as reference; nothing copied into the tree).
'use strict';
const { spawn } = require('node:child_process');
const fs = require('node:fs');

// TestDefaults.BrowserHang, read from tests/BrowserAI.Tests/Harness/TestDefaults.cs:118
// = TimeSpan.FromMinutes(30). Every wait in these probes is bounded by it.
const BROWSER_HANG_MS = 30 * 60 * 1000;

function makeClient(exe, args, opts) {
  opts = opts || {};
  const child = spawn(exe, args || [], {
    cwd: opts.cwd,
    env: opts.env || process.env,
    stdio: ['pipe', 'pipe', 'pipe'],
    windowsHide: true,          // CreateNoWindow equivalent (repo rule)
  });
  const errStream = opts.stderrLog ? fs.createWriteStream(opts.stderrLog, { flags: 'w' }) : null;
  child.stderr.setEncoding('utf8');
  child.stderr.on('data', d => { if (errStream) errStream.write(d); });

  let buf = '';
  const pending = new Map();
  const notifications = [];
  child.stdout.setEncoding('utf8');
  child.stdout.on('data', chunk => {
    buf += chunk;
    let i;
    while ((i = buf.indexOf('\n')) >= 0) {
      const line = buf.slice(0, i).trim();
      buf = buf.slice(i + 1);
      if (!line) continue;
      let msg;
      try { msg = JSON.parse(line); } catch { continue; }
      if (msg.id !== undefined && pending.has(msg.id)) {
        const e = pending.get(msg.id);
        pending.delete(msg.id);
        e.resolve({ msg, ms: Date.now() - e.t0 });
      } else if (msg.method) {
        notifications.push({ at: Date.now(), method: msg.method });
      }
    }
  });

  let nextId = 0;
  function rpc(method, params, timeoutMs) {
    const id = ++nextId;
    const frame = { jsonrpc: '2.0', id, method };
    if (params !== undefined) frame.params = params;
    const t0 = Date.now();
    const budget = timeoutMs === undefined ? BROWSER_HANG_MS : timeoutMs;
    const p = new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        pending.delete(id);
        reject(Object.assign(new Error(`'${method}' (id ${id}) unanswered after ${Date.now() - t0} ms`), { timedOut: true, ms: Date.now() - t0 }));
      }, budget);
      pending.set(id, { t0, resolve: v => { clearTimeout(timer); resolve(v); } });
    });
    child.stdin.write(JSON.stringify(frame) + '\n');
    return p;
  }
  function notify(method, params) {
    const frame = { jsonrpc: '2.0', method };
    if (params !== undefined) frame.params = params;
    child.stdin.write(JSON.stringify(frame) + '\n');
  }
  return { child, rpc, notify, notifications, BROWSER_HANG_MS };
}

module.exports = { makeClient, BROWSER_HANG_MS };
