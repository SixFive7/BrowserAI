// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A minimal MCP client for BrowserAI.Server.exe over stdio, plus the process
// census the rigs record: every descendant of the server pid this client
// started, read by parent-pid lineage and never by an image name.

import { spawn, execFileSync } from 'node:child_process';
import { appendFileSync, createWriteStream } from 'node:fs';

export class Client {
  constructor(exe, { cwd, stderrPath, logPath, env }) {
    this.logPath = logPath;
    this.child = spawn(exe, [], { cwd, env: env ?? process.env, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    this.pid = this.child.pid;
    this.child.stderr.pipe(createWriteStream(stderrPath));
    this.waiting = new Map();
    this.nextId = 1;
    this.exited = new Promise((resolve) => this.child.on('exit', (code) => resolve(code)));
    let pending = '';
    this.child.stdout.setEncoding('utf8');
    this.child.stdout.on('data', (chunk) => {
      pending += chunk;
      for (let end = pending.indexOf('\n'); end >= 0; end = pending.indexOf('\n')) {
        const line = pending.slice(0, end).trim();
        pending = pending.slice(end + 1);
        if (!line) continue;
        let message;
        try { message = JSON.parse(line); } catch { this.note('unparsed', line.slice(0, 300)); continue; }
        if (message.id !== undefined && this.waiting.has(message.id)) {
          this.waiting.get(message.id)(message);
          this.waiting.delete(message.id);
        } else if (message.method) {
          this.note('notification', message.method);
        }
      }
    });
  }

  note(kind, text) {
    appendFileSync(this.logPath, `${new Date().toISOString()} ${kind} ${text}\n`);
  }

  request(method, params, timeoutMs = 240000) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.waiting.delete(id); reject(new Error(`${method} timed out after ${timeoutMs} ms`)); }, timeoutMs);
      this.waiting.set(id, (message) => { clearTimeout(timer); resolve(message); });
      this.child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
    });
  }

  notify(method, params) {
    this.child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method, ...(params ? { params } : {}) })}\n`);
  }

  async handshake() {
    const init = await this.request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'behave-rig', version: '1' } });
    this.notify('notifications/initialized');
    const list = await this.request('tools/list', {});
    return { init, toolCount: list.result?.tools?.length ?? 0, tools: list.result?.tools?.map((t) => t.name) ?? [] };
  }

  // One tool call; returns { ok, isError, text, ms, images }.
  async call(name, args, timeoutMs) {
    const started = Date.now();
    const message = await this.request('tools/call', { name, arguments: args }, timeoutMs);
    const ms = Date.now() - started;
    if (message.error) {
      const text = `JSON-RPC error ${message.error.code}: ${message.error.message}`;
      this.note('call', `${name} ${ms}ms RPC-ERROR ${text.slice(0, 200)}`);
      return { ok: false, isError: true, text, ms, images: 0 };
    }
    const content = message.result?.content ?? [];
    const text = content.filter((c) => c.type === 'text').map((c) => c.text).join('\n');
    const images = content.filter((c) => c.type === 'image').length;
    const isError = !!message.result?.isError;
    this.note('call', `${name} ${ms}ms isError=${isError} ${text.slice(0, 160).replace(/\s+/g, ' ')}`);
    return { ok: !isError, isError, text, ms, images };
  }

  async close() {
    try { this.child.stdin.end(); } catch { /* gone */ }
    const code = await Promise.race([this.exited, new Promise((r) => setTimeout(() => r('timeout'), 60000))]);
    return code;
  }
}

// The section of an upstream answer under "### <title>", up to the next header.
export function section(text, title) {
  const at = text.indexOf(`### ${title}\n`);
  if (at < 0) return null;
  const rest = text.slice(at + title.length + 5);
  const end = rest.search(/\n### /);
  return (end < 0 ? rest : rest.slice(0, end)).trim();
}

// The value a browser_evaluate answer carries, parsed: the function returns
// JSON.stringify(...) so the result section is a JSON string literal.
export function evaluated(text) {
  const raw = section(text, 'Result');
  if (raw === null) return { error: text.slice(0, 400) };
  try {
    let value = JSON.parse(raw);
    if (typeof value === 'string') { try { value = JSON.parse(value); } catch { /* plain string */ } }
    return value;
  } catch {
    return { unparsed: raw.slice(0, 400) };
  }
}

export function tabsOf(text) {
  const tabs = [];
  for (const line of text.split('\n')) {
    const m = line.match(/^- (\d+):( \(current\))? \[(.*)\]\((.*)\)( \[crashed\])?$/);
    if (m) tabs.push({ index: Number(m[1]), current: !!m[2], title: m[3], url: m[4], crashed: !!m[5] });
  }
  return tabs;
}

// Every descendant of a pid, by parent-pid lineage, with its image path and
// command line. Observation only.
export function descendants(rootPid) {
  const json = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command',
    'Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,ExecutablePath,CommandLine,@{n="Created";e={$_.CreationDate.ToFileTimeUtc()}} | ConvertTo-Json -Compress -Depth 2'],
  { encoding: 'utf8', windowsHide: true, maxBuffer: 64 * 1024 * 1024 });
  const all = JSON.parse(json);
  const byParent = new Map();
  for (const p of all) {
    if (!byParent.has(p.ParentProcessId)) byParent.set(p.ParentProcessId, []);
    byParent.get(p.ParentProcessId).push(p);
  }
  const found = [];
  const walk = (pid, depth) => {
    for (const child of byParent.get(pid) ?? []) {
      if (child.ProcessId === pid) continue;
      found.push({ pid: child.ProcessId, parent: pid, depth, exe: child.ExecutablePath, cmd: child.CommandLine, created: child.Created });
      walk(child.ProcessId, depth + 1);
    }
  };
  walk(rootPid, 1);
  return found;
}

// What the census says about the browser: its main process's image and the
// switches that decide the mode, and how many processes of each kind.
export function browserProgram(procs) {
  const browsers = procs.filter((p) => p.exe && /\\(chrome|firefox|chrome-headless-shell|headless_shell)\.exe$/i.test(p.exe));
  const main = browsers.find((p) => !/--type=|-contentproc/.test(p.cmd ?? '')) ?? null;
  const kinds = {};
  for (const p of browsers) {
    const t = (p.cmd ?? '').match(/--type=([a-z-]+)/)?.[1] ?? ((p.cmd ?? '').includes('-contentproc') ? 'contentproc' : 'main');
    kinds[t] = (kinds[t] ?? 0) + 1;
  }
  const cmd = main?.cmd ?? '';
  const flags = (cmd.match(/(?:^|\s)(--headless(?:=\w+)?|-headless|--remote-debugging-pipe|-juggler-pipe|--user-data-dir=[^\s"]+|-profile)/g) ?? []).map((s) => s.trim());
  return { mainExe: main?.exe ?? null, mainPid: main?.pid ?? null, mainCreated: main?.created ?? null, flags, kinds, count: browsers.length };
}
