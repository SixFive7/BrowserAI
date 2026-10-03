// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Per-action latency, local, headless, same CfT binary: click and type, library-level and MCP-level,
// plus Stagehand's batch. Every click is checked against the page's own counter. Scratch only.
import path from "node:path";
import { startPwMcp } from "./pwmcp.mjs";
import { McpClient } from "./mcpclient.mjs";
import { applyScratchEnvToSelf, startFixtureServer, now, stats, writeJson, BENCH, CHROME } from "./lib.mjs";
applyScratchEnvToSelf();
const { chromium } = await import("./pw/node_modules/playwright-core/index.mjs");
const { localBrowser, Stagehand } = await import("./sh/node_modules/@browserbasehq/stagehand/dist/index.mjs");
const { server, base } = await startFixtureServer();
const url = base + "/form.html";
const CLICKS = 30, TYPES = 10, MCP_CLICKS = 15, MCP_TYPES = 5;
const text = "hello world from the zoom-out";
const r = {};

// playwright-core, in-process
{
  const ctx = await chromium.launchPersistentContext(path.join(BENCH, "profiles", "pw-action"), { channel: "chrome-for-testing", headless: true, viewport: { width: 1920, height: 1080 } });
  const page = ctx.pages()[0] || (await ctx.newPage());
  await page.goto(url);
  const click = [], fill = [], type = [];
  for (let i = 0; i < CLICKS; i++) { const t = now(); await page.locator("#counter").click(); click.push(now() - t); }
  const count = await page.locator("#count").innerText();
  for (let i = 0; i < TYPES; i++) { const t = now(); await page.locator("#email").fill("a" + i + "@example.com"); fill.push(now() - t); }
  for (let i = 0; i < TYPES; i++) { await page.locator("#email").fill(""); const t = now(); await page.locator("#email").pressSequentially(text); type.push(now() - t); }
  const value = await page.locator("#email").inputValue();
  r.pwLib = { click: stats(click), fill: stats(fill), typeKeys: stats(type), countAfterClicks: count, valueAfterType: value };
  await ctx.close();
}
// Stagehand, in-process
{
  const browser = await localBrowser.launch({ executablePath: CHROME, headless: true, userDataDir: path.join(BENCH, "profiles", "sh-action"), viewport: { width: 1920, height: 1080 } });
  const stagehand = await Stagehand.create({ browser, logging: { level: "error" } });
  const page = await browser.context.activePage();
  await page.goto(url, { waitUntil: "load" });
  const click = [], fill = [], type = [];
  for (let i = 0; i < CLICKS; i++) { const t = now(); await page.locator("#counter").click(); click.push(now() - t); }
  const count = await page.locator("#count").innerText();
  for (let i = 0; i < TYPES; i++) { const t = now(); await page.locator("#email").fill("a" + i + "@example.com"); fill.push(now() - t); }
  for (let i = 0; i < TYPES; i++) { await page.locator("#email").fill(""); const t = now(); await page.locator("#email").type(text); type.push(now() - t); }
  const value = await page.locator("#email").inputValue();
  // the batch: 30 clicks in one round trip, run next to the page inside the extension
  const tb = now();
  const batch = await stagehand.experimentalBatch(async (s, input) => {
    for (let i = 0; i < input.n; i++) await s.page.locator("#counter").click();
    return await s.page.locator("#count").innerText();
  }, { n: CLICKS }, { page, timeout: 60000 });
  const batchMs = now() - tb;
  r.shLib = { click: stats(click), fill: stats(fill), typeKeys: stats(type), countAfterClicks: count, valueAfterType: value, batch30ClicksMs: +batchMs.toFixed(1), countAfterBatch: batch };
  await stagehand.close();
  await browser.close();
}
// Playwright MCP, the way BrowserAI exposes it (ref from a snapshot, settle wait, response formatting)
{
  const c = await startPwMcp("action");
  await c.call("browser_navigate", { url });
  const snap = McpClient.text(await c.call("browser_snapshot", {}));
  const ref = (re) => (snap.match(re) || [])[1];
  const countRef = ref(/button "Count" \[ref=(e\d+)\]/), emailRef = ref(/textbox "Email address" \[ref=(e\d+)\]/);
  const click = [], type = [];
  let lastClickChars = 0;
  for (let i = 0; i < MCP_CLICKS; i++) { const t = now(); const resp = await c.call("browser_click", { element: "Count button", target: countRef }); click.push(now() - t); lastClickChars = McpClient.text(resp).length; if (resp.result?.isError) throw new Error(McpClient.text(resp)); }
  for (let i = 0; i < MCP_TYPES; i++) { const t = now(); const resp = await c.call("browser_type", { element: "Email", target: emailRef, text }); type.push(now() - t); if (resp.result?.isError) throw new Error(McpClient.text(resp)); }
  const ev = McpClient.text(await c.call("browser_evaluate", { function: "() => document.getElementById('count').textContent" }));
  r.pwMcp = { click: stats(click), type: stats(type), refs: { countRef, emailRef }, clickResponseChars: lastClickChars, evaluateAfterClicks: ev.slice(0, 200) };
  await c.call("browser_close", {});
  await c.close();
}
server.close();
writeJson("action.json", r);
console.log(JSON.stringify(r, null, 1));
