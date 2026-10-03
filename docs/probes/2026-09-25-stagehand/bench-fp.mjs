// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// What a page can see: Playwright MCP (BrowserAI config) vs Stagehand vs the same CfT binary with no automation
// client at all (--headless --dump-dom). Plus a net log of the Stagehand run, to see the default telemetry.
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { startPwMcp } from "./pwmcp.mjs";
import { McpClient } from "./mcpclient.mjs";
import { applyScratchEnvToSelf, scratchEnv, startFixtureServer, writeJson, BENCH, CHROME, OUT, sleep } from "./lib.mjs";
applyScratchEnvToSelf();
const { localBrowser, Stagehand } = await import("./sh/node_modules/@browserbasehq/stagehand/dist/index.mjs");
const { server, base } = await startFixtureServer();
const url = base + "/fp.html";
const out = {};

// Playwright MCP
{
  const c = await startPwMcp("fp");
  await c.call("browser_navigate", { url });
  await sleep(1500);
  const t = McpClient.text(await c.call("browser_evaluate", { function: "() => document.getElementById('fp').textContent" }));
  const m = t.match(/### Result\n([\s\S]*?)(\n###|$)/);
  try { out.playwrightMcp = JSON.parse(JSON.parse(m[1].trim())); } catch { out.playwrightMcp = { raw: t.slice(0, 2000) }; }
  await c.call("browser_close", {});
  await c.close();
}
// Stagehand, with a net log
{
  const netlog = path.join(OUT, "sh-fp-netlog.json");
  const browser = await localBrowser.launch({ executablePath: CHROME, headless: true, userDataDir: path.join(BENCH, "profiles", "sh-fp"), viewport: { width: 1920, height: 1080 }, args: [`--log-net-log=${netlog}`, "--net-log-capture-mode=Default"] });
  const stagehand = await Stagehand.create({ browser, logging: { level: "error" } });
  const page = await browser.context.activePage();
  await page.goto(url, { waitUntil: "load" });
  await sleep(1500);
  out.stagehand = JSON.parse(await page.evaluate(() => document.getElementById("fp").textContent));
  await page.snapshot();
  await page.locator("#fp").click();
  await sleep(8000); // longer than the extension's 1 s batch + 5 s export timeout
  await stagehand.close();
  await browser.close();
  const log = fs.existsSync(netlog) ? fs.readFileSync(netlog, "utf8") : "";
  out.stagehandNetlog = { bytes: log.length, mentionsExampleCom: (log.match(/example\.com/g) || []).length, v1traces: (log.match(/v1\/traces/g) || []).length, hosts: [...new Set((log.match(/https?:\/\/[a-z0-9.-]+/gi) || []).map((u) => u.toLowerCase()))].slice(0, 40) };
}
// No automation client: the same binary, headless, dump the DOM after running the page's timers
{
  const r = spawnSync(CHROME, ["--headless", `--user-data-dir=${path.join(BENCH, "profiles", "control-dumpdom")}`, "--virtual-time-budget=3000", "--dump-dom", url], { env: scratchEnv(), windowsHide: true, timeout: 60000, maxBuffer: 16 * 1024 * 1024 });
  const html = r.stdout.toString();
  const m = html.match(/<pre id="fp">([\s\S]*?)<\/pre>/);
  const decode = (s) => s.replace(/&quot;/g, '"').replace(/&amp;/g, "&").replace(/&lt;/g, "<").replace(/&gt;/g, ">");
  out.noAutomationControl = m ? JSON.parse(decode(m[1])) : { status: r.status, err: r.stderr.toString().slice(-500) };
}
server.close();
writeJson("fp.json", out);
const keys = ["webdriver", "cdpConsoleStackGetterRead", "ua", "brands", "fullVersionList", "plugins", "mimeTypes", "chrome", "chromeKeys", "notification", "perm", "screen", "window", "webgl", "suspiciousGlobals", "htmlAttributes", "domAttrsAfter", "languages", "done"];
for (const k of keys) console.log(k.padEnd(26), "| PW-MCP:", JSON.stringify(out.playwrightMcp?.[k]), "| SH:", JSON.stringify(out.stagehand?.[k]), "| none:", JSON.stringify(out.noAutomationControl?.[k]));
console.log("netlog", JSON.stringify(out.stagehandNetlog));
