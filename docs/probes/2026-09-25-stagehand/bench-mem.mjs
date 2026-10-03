// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Memory after loading the same pages, fresh launch each time, per process role. Scratch only.
import path from "node:path";
import { execFileSync } from "node:child_process";
import { startPwMcp } from "./pwmcp.mjs";
import { applyScratchEnvToSelf, startFixtureServer, writeJson, BENCH, CHROME, sleep } from "./lib.mjs";
applyScratchEnvToSelf();
const { localBrowser, Stagehand } = await import("./sh/node_modules/@browserbasehq/stagehand/dist/index.mjs");

function table() {
  const ps = "$all = Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,WorkingSetSize,PrivatePageCount,Name,CommandLine; @{ self = $PID; procs = $all } | ConvertTo-Json -Compress -Depth 4";
  const j = JSON.parse(execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", ps], { windowsHide: true, maxBuffer: 128 * 1024 * 1024 }).toString());
  const ex = new Set([j.self]); let g = true;
  while (g) { g = false; for (const p of j.procs) if (!ex.has(p.ProcessId) && ex.has(p.ParentProcessId)) { ex.add(p.ProcessId); g = true; } }
  return j.procs.filter((p) => !ex.has(p.ProcessId));
}
function tree(root) {
  const t = table(); const out = []; const st = [root]; const seen = new Set([root]);
  const r = t.find((p) => p.ProcessId === root); if (r) out.push(r);
  while (st.length) { const pid = st.pop(); for (const c of t.filter((p) => p.ParentProcessId === pid)) if (!seen.has(c.ProcessId)) { seen.add(c.ProcessId); out.push(c); st.push(c.ProcessId); } }
  return out;
}
function role(p) {
  const c = p.CommandLine || "";
  if (/node(\.exe)?$/i.test(p.Name) || p.Name === "node.exe") return "node (driver/host)";
  if (p.Name !== "chrome.exe") return p.Name;
  if (!/--type=/.test(c)) return "chrome browser";
  if (/--extension-process/.test(c)) return "chrome extension renderer";
  const m = c.match(/--type=([a-z-]+)/); let t = m ? m[1] : "?";
  if (t === "utility") { const s = c.match(/--utility-sub-type=([\w.]+)/); t += s ? ":" + s[1].split(".").pop() : ""; }
  return "chrome " + t;
}
function breakdown(procs) {
  const by = {};
  for (const p of procs) { const k = role(p); by[k] ??= { n: 0, privMiB: 0, wsMiB: 0 }; by[k].n++; by[k].privMiB += Number(p.PrivatePageCount) / 1048576; by[k].wsMiB += Number(p.WorkingSetSize) / 1048576; }
  for (const v of Object.values(by)) { v.privMiB = +v.privMiB.toFixed(1); v.wsMiB = +v.wsMiB.toFixed(1); }
  const tot = procs.reduce((a, p) => a + Number(p.PrivatePageCount), 0) / 1048576;
  const totWs = procs.reduce((a, p) => a + Number(p.WorkingSetSize), 0) / 1048576;
  return { total: { n: procs.length, privMiB: +tot.toFixed(1), wsMiB: +totWs.toFixed(1) }, by };
}
const { server, base } = await startFixtureServer();
const targets = [["form", base + "/form.html"], ["table", base + "/table.html"], ["wikipedia", "https://en.wikipedia.org/wiki/Playwright_(software)"]];
const out = { pw: [], sh: [] };
for (let rep = 0; rep < 3; rep++) {
  for (const [name, url] of targets) {
    { // Playwright MCP, BrowserAI configuration
      const c = await startPwMcp(`mem-${rep}-${name}`);
      await c.call("browser_navigate", { url });
      await c.call("browser_snapshot", {});
      await sleep(3000);
      out.pw.push({ rep, name, ...breakdown(tree(c.pid)) });
      await c.call("browser_close", {});
      await c.close();
    }
    { // Stagehand; the driver is this node process, so its own share is reported as the host
      const browser = await localBrowser.launch({ executablePath: CHROME, headless: true, userDataDir: path.join(BENCH, "profiles", `sh-mem-${rep}-${name}`), viewport: { width: 1920, height: 1080 } });
      const stagehand = await Stagehand.create({ browser, logging: { level: "error" } });
      const page = await browser.context.activePage();
      await page.goto(url, { waitUntil: "load" });
      await page.snapshot();
      await sleep(3000);
      out.sh.push({ rep, name, ...breakdown(tree(process.pid)) });
      await stagehand.close();
      await browser.close();
    }
    process.stderr.write(`${rep} ${name} pw ${out.pw.at(-1).total.privMiB} sh ${out.sh.at(-1).total.privMiB}\n`);
  }
}
server.close();
writeJson("mem.json", out);
console.log(JSON.stringify(out, null, 1));
