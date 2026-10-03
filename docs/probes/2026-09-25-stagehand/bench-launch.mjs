// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Cold-process, warm-profile launch-to-first-page: Playwright MCP (spawned, as BrowserAI does), Stagehand in a
// spawned host process, and both libraries in-process. Same CfT 154 binary, headless, 1920x1080. Scratch only.
import path from "node:path";
import { spawn } from "node:child_process";
import { startPwMcp } from "./pwmcp.mjs";
import { McpClient } from "./mcpclient.mjs";
import { applyScratchEnvToSelf, scratchEnv, startFixtureServer, now, stats, writeJson, BENCH, CHROME } from "./lib.mjs";
applyScratchEnvToSelf();
const { chromium } = await import("./pw/node_modules/playwright-core/index.mjs");
const { localBrowser, Stagehand } = await import("./sh/node_modules/@browserbasehq/stagehand/dist/index.mjs");

const { server, base } = await startFixtureServer();
const url = base + "/blank.html";
const N = 5;
const res = { pwMcpSpawned: [], shSpawned: [], pwLib: [], shLib: [], shSpawnedParts: [] };

async function pwMcpOnce() {
  const t0 = now();
  const c = await startPwMcp("launch");
  const nav = await c.call("browser_navigate", { url });
  const t1 = now();
  if (nav.result?.isError) throw new Error("nav error " + McpClient.text(nav));
  await c.call("browser_close", {});
  await c.close();
  return t1 - t0;
}
function shSpawnedOnce() {
  return new Promise((resolve, reject) => {
    const t0 = now();
    const p = spawn(process.execPath, [path.join(BENCH, "launch-sh-child.mjs"), url, "sh-launch"], { env: scratchEnv(), cwd: BENCH, stdio: ["ignore", "pipe", "pipe"], windowsHide: true });
    let out = "", err = "", t1;
    p.stdout.on("data", (d) => { out += d; if (t1 === undefined && out.includes("\n")) t1 = now(); });
    p.stderr.on("data", (d) => { err += d; });
    p.on("exit", (code) => { if (code !== 0 || t1 === undefined) reject(new Error("child failed " + code + " " + err.slice(-500))); else resolve({ ms: t1 - t0, parts: JSON.parse(out.split("\n")[0]) }); });
  });
}
async function pwLibOnce() {
  const t0 = now();
  const ctx = await chromium.launchPersistentContext(path.join(BENCH, "profiles", "pwlib-launch"), { channel: "chrome-for-testing", headless: true, viewport: { width: 1920, height: 1080 } });
  const page = ctx.pages()[0] || (await ctx.newPage());
  await page.goto(url, { waitUntil: "load" });
  const t1 = now();
  await ctx.close();
  return t1 - t0;
}
async function shLibOnce() {
  const t0 = now();
  const browser = await localBrowser.launch({ executablePath: CHROME, headless: true, userDataDir: path.join(BENCH, "profiles", "shlib-launch"), viewport: { width: 1920, height: 1080 } });
  const stagehand = await Stagehand.create({ browser, logging: { level: "error" } });
  const page = await browser.context.activePage();
  await page.goto(url, { waitUntil: "load" });
  const t1 = now();
  await stagehand.close();
  await browser.close();
  return t1 - t0;
}
const runners = [
  ["pwMcpSpawned", pwMcpOnce],
  ["shSpawned", async () => { const r = await shSpawnedOnce(); res.shSpawnedParts.push(r.parts); return r.ms; }],
  ["pwLib", pwLibOnce],
  ["shLib", shLibOnce],
];
// warm-up (profiles created, disk cache warm); not recorded
for (const [, fn] of runners) await fn();
res.shSpawnedParts.length = 0;
for (let i = 0; i < N; i++) {
  const order = runners.slice(i % runners.length).concat(runners.slice(0, i % runners.length));
  for (const [k, fn] of order) { const ms = await fn(); res[k].push(ms); process.stderr.write(`${i} ${k} ${ms.toFixed(0)}\n`); }
}
server.close();
const summary = Object.fromEntries(Object.entries(res).filter(([k]) => k !== "shSpawnedParts").map(([k, v]) => [k, stats(v)]));
writeJson("launch.json", { res, summary });
console.log(JSON.stringify(summary, null, 1));
console.log("shSpawnedParts", JSON.stringify(res.shSpawnedParts.map((p) => Object.fromEntries(Object.entries(p).map(([k, v]) => [k, Math.round(v)])))));
