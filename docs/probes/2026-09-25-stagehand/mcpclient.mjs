// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Minimal MCP stdio client (newline-delimited JSON-RPC). Scratch only.
import { spawn } from "node:child_process";

export class McpClient {
  constructor(command, args, env, cwd) {
    this.proc = spawn(command, args, { env, cwd, stdio: ["pipe", "pipe", "pipe"], windowsHide: true });
    this.pid = this.proc.pid;
    this.nextId = 1;
    this.pending = new Map();
    this.buf = "";
    this.stderr = "";
    this.proc.stdout.setEncoding("utf8");
    this.proc.stdout.on("data", (d) => {
      this.buf += d;
      let i;
      while ((i = this.buf.indexOf("\n")) >= 0) {
        const line = this.buf.slice(0, i).trim();
        this.buf = this.buf.slice(i + 1);
        if (!line) continue;
        let msg;
        try { msg = JSON.parse(line); } catch { continue; }
        if (msg.id !== undefined && this.pending.has(msg.id)) {
          const { resolve } = this.pending.get(msg.id);
          this.pending.delete(msg.id);
          resolve(msg);
        }
      }
    });
    this.proc.stderr.setEncoding("utf8");
    this.proc.stderr.on("data", (d) => { this.stderr += d; if (this.stderr.length > 200000) this.stderr = this.stderr.slice(-100000); });
    this.exited = new Promise((r) => this.proc.once("exit", (code, sig) => r({ code, sig })));
  }
  request(method, params) {
    const id = this.nextId++;
    const p = new Promise((resolve, reject) => { this.pending.set(id, { resolve, reject }); });
    this.proc.stdin.write(JSON.stringify({ jsonrpc: "2.0", id, method, params }) + "\n");
    return p;
  }
  notify(method, params) { this.proc.stdin.write(JSON.stringify({ jsonrpc: "2.0", method, params }) + "\n"); }
  async initialize() {
    const r = await this.request("initialize", { protocolVersion: "2025-06-18", capabilities: {}, clientInfo: { name: "zoomout-d-bench", version: "0" } });
    this.notify("notifications/initialized", {});
    return r;
  }
  async call(name, args = {}) {
    const r = await this.request("tools/call", { name, arguments: args });
    return r;
  }
  static text(resp) {
    const c = resp?.result?.content || [];
    return c.filter((x) => x.type === "text").map((x) => x.text).join("\n");
  }
  async close() {
    try { this.proc.stdin.end(); } catch {}
    const t = setTimeout(() => { try { this.proc.kill(); } catch {} }, 5000);
    await this.exited;
    clearTimeout(t);
  }
}
