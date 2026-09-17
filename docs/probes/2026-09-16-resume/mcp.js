// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// A minimal JSON-RPC-over-stdio client for BrowserAI.Server.exe.
// Newline-delimited JSON, UTF-8: the framing StdioChannel owns.
'use strict';
const { spawn } = require('node:child_process');

class Mcp {
  constructor(exe, env) {
    this.proc = spawn(exe, [], {
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true,                 // house rule: no console window
      env: { ...process.env, ...(env || {}) },
    });
    this.next = 1;
    this.pending = new Map();
    this.stderr = '';
    this.buffer = '';
    this.proc.stdout.setEncoding('utf8');
    this.proc.stderr.setEncoding('utf8');
    this.proc.stderr.on('data', d => { this.stderr += d; });
    this.proc.stdout.on('data', d => {
      this.buffer += d;
      let i;
      while ((i = this.buffer.indexOf('\n')) >= 0) {
        const line = this.buffer.slice(0, i).trim();
        this.buffer = this.buffer.slice(i + 1);
        if (!line) continue;
        let msg; try { msg = JSON.parse(line); } catch { continue; }
        if (msg.id !== undefined && this.pending.has(msg.id)) {
          const { resolve } = this.pending.get(msg.id);
          this.pending.delete(msg.id);
          resolve(msg);
        }
      }
    });
  }

  send(method, params) {
    const id = this.next++;
    const started = process.hrtime.bigint();
    const p = new Promise(resolve => this.pending.set(id, { resolve }));
    this.proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
    return p.then(msg => ({ ...msg, ms: Number(process.hrtime.bigint() - started) / 1e6 }));
  }

  notify(method, params) {
    this.proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', method, params }) + '\n');
  }

  async initialize() {
    const r = await this.send('initialize', {
      protocolVersion: '2025-06-18',
      capabilities: {},
      clientInfo: { name: 'reverify-probe', version: '2026-09-16' },
    });
    this.notify('notifications/initialized', {});
    return r;
  }

  async tool(name, args) {
    const r = await this.send('tools/call', { name, arguments: args });
    return r;
  }

  close() { try { this.proc.stdin.end(); } catch { /* already gone */ } }
}

function text(answer) {
  const c = answer && answer.result && answer.result.content;
  if (!Array.isArray(c)) return JSON.stringify(answer && (answer.error || answer.result));
  return c.filter(b => b.type === 'text').map(b => b.text).join('\n');
}

module.exports = { Mcp, text };
