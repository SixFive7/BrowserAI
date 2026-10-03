// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Shared helpers for the zoom-out track D benchmark. Scratch only.
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

export const BENCH = path.dirname(fileURLToPath(import.meta.url));
export const FIXTURES = path.join(BENCH, "fixtures");
export const OUT = path.join(BENCH, "out");
export const CHROME = path.join(BENCH, "browsers", "chromium-1246", "chrome-win64", "chrome.exe");

// Every Playwright launch points LOCALAPPDATA (and the server registry) at scratch,
// so no descriptor lands in the real %LOCALAPPDATA%\ms-playwright\b.
export function scratchEnv(extra = {}) {
  return {
    ...process.env,
    LOCALAPPDATA: path.join(BENCH, "localappdata"),
    PWTEST_SERVER_REGISTRY: path.join(BENCH, "pwregistry"),
    PLAYWRIGHT_BROWSERS_PATH: path.join(BENCH, "browsers"),
    TEMP: path.join(BENCH, "tmp"),
    TMP: path.join(BENCH, "tmp"),
    ...extra,
  };
}

export function applyScratchEnvToSelf() {
  const env = scratchEnv();
  for (const k of ["LOCALAPPDATA", "PWTEST_SERVER_REGISTRY", "PLAYWRIGHT_BROWSERS_PATH", "TEMP", "TMP"]) process.env[k] = env[k];
}

export function startFixtureServer() {
  const types = { ".html": "text/html; charset=utf-8", ".js": "text/javascript" };
  const server = http.createServer((req, res) => {
    const u = new URL(req.url, "http://127.0.0.1");
    const p = path.join(FIXTURES, path.normalize(decodeURIComponent(u.pathname)).replace(/^([/\\])+/, ""));
    if (!p.startsWith(FIXTURES) || !fs.existsSync(p) || fs.statSync(p).isDirectory()) { res.writeHead(404); res.end("nf"); return; }
    res.writeHead(200, { "content-type": types[path.extname(p)] || "application/octet-stream", "cache-control": "no-store" });
    fs.createReadStream(p).pipe(res);
  });
  return new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve({ server, base: `http://127.0.0.1:${server.address().port}` })));
}

// Process tree rooted at a pid, walked by ParentProcessId. Never by image name.
export function processTable() {
  // The measuring PowerShell reports its own pid ($PID) so its subtree (itself and its
  // console host) is excluded from every sum by pid, not by name.
  const ps = "$all = Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,WorkingSetSize,PrivatePageCount,Name; @{ self = $PID; procs = $all } | ConvertTo-Json -Compress -Depth 4";
  const raw = execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", ps], { windowsHide: true, maxBuffer: 64 * 1024 * 1024 }).toString();
  const j = JSON.parse(raw);
  const exclude = new Set([j.self]);
  let grew = true;
  while (grew) { grew = false; for (const p of j.procs) { if (!exclude.has(p.ProcessId) && exclude.has(p.ParentProcessId)) { exclude.add(p.ProcessId); grew = true; } } }
  return j.procs.filter((p) => !exclude.has(p.ProcessId));
}

export function treeOf(rootPid, table = processTable()) {
  const kids = new Map();
  for (const p of table) { if (!kids.has(p.ParentProcessId)) kids.set(p.ParentProcessId, []); kids.get(p.ParentProcessId).push(p); }
  const root = table.find((p) => p.ProcessId === rootPid);
  const out = root ? [root] : [];
  const stack = [rootPid];
  const seen = new Set([rootPid]);
  while (stack.length) {
    const pid = stack.pop();
    for (const c of kids.get(pid) || []) { if (seen.has(c.ProcessId)) continue; seen.add(c.ProcessId); out.push(c); stack.push(c.ProcessId); }
  }
  return out;
}

export function summarize(procs) {
  const ws = procs.reduce((a, p) => a + Number(p.WorkingSetSize || 0), 0);
  const priv = procs.reduce((a, p) => a + Number(p.PrivatePageCount || 0), 0);
  const byName = {};
  for (const p of procs) { byName[p.Name] = (byName[p.Name] || 0) + 1; }
  return { count: procs.length, workingSetMiB: +(ws / 1048576).toFixed(1), privateMiB: +(priv / 1048576).toFixed(1), byName };
}

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
export const now = () => performance.now();
export function stats(xs) {
  const s = [...xs].sort((a, b) => a - b);
  const q = (f) => s[Math.min(s.length - 1, Math.floor(f * (s.length - 1) + 0.5))];
  const mean = s.reduce((a, b) => a + b, 0) / s.length;
  return { n: s.length, min: +s[0].toFixed(1), median: +q(0.5).toFixed(1), p90: +q(0.9).toFixed(1), max: +s[s.length - 1].toFixed(1), mean: +mean.toFixed(1) };
}
export function withTimeout(promise, ms, label) {
  let t;
  return Promise.race([promise, new Promise((_, rej) => { t = setTimeout(() => rej(new Error(`TIMEOUT ${label} after ${ms} ms`)), ms); })]).finally(() => clearTimeout(t));
}
export function writeJson(name, obj) { fs.writeFileSync(path.join(OUT, name), JSON.stringify(obj, null, 1)); }
