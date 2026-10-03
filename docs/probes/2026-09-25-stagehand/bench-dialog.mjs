// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// What happens when a page opens alert(): Stagehand has no dialog API; Playwright MCP has browser_handle_dialog.
import path from "node:path";
import { startPwMcp } from "./pwmcp.mjs";
import { McpClient } from "./mcpclient.mjs";
import { applyScratchEnvToSelf, startFixtureServer, now, writeJson, BENCH, CHROME, withTimeout } from "./lib.mjs";
applyScratchEnvToSelf();
const { localBrowser, Stagehand } = await import("./sh/node_modules/@browserbasehq/stagehand/dist/index.mjs");
const { server, base } = await startFixtureServer();
const url = base + "/dialog.html";
const out = { stagehand: [], playwrightMcp: [] };
const step = async (arr, label, fn, ms) => {
  const t = now();
  try { const v = await withTimeout(fn(), ms, label); arr.push({ label, ok: true, ms: Math.round(now() - t), value: typeof v === "string" ? v.slice(0, 600) : v }); }
  catch (e) { arr.push({ label, ok: false, ms: Math.round(now() - t), error: String(e.message || e).slice(0, 400) }); }
};
{
  const browser = await localBrowser.launch({ executablePath: CHROME, headless: true, userDataDir: path.join(BENCH, "profiles", "sh-dialog"), viewport: { width: 1920, height: 1080 } });
  const stagehand = await Stagehand.create({ browser, logging: { level: "error" } });
  const page = await browser.context.activePage();
  await page.goto(url, { waitUntil: "load" });
  const s = out.stagehand;
  await step(s, "click #alert", () => page.locator("#alert").click(), 20000);
  await step(s, "read #after", () => page.locator("#after").innerText(), 15000);
  await step(s, "evaluate document.title", () => page.evaluate(() => document.title), 15000);
  await step(s, "click #confirm", () => page.locator("#confirm").click(), 20000);
  await step(s, "read #after (2)", () => page.locator("#after").innerText(), 15000);
  await step(s, "goto blank", () => page.goto(base + "/blank.html", { waitUntil: "load", timeout: 15000 }).then(() => "navigated"), 20000);
  await step(s, "snapshot after", () => page.snapshot().then((x) => x.formattedTree), 15000);
  try { await withTimeout(stagehand.close(), 15000, "stagehand.close"); } catch (e) { s.push({ label: "stagehand.close", ok: false, error: e.message }); }
  try { await withTimeout(browser.close(), 20000, "browser.close"); } catch (e) { s.push({ label: "browser.close", ok: false, error: e.message }); }
}
{
  const c = await startPwMcp("dialog");
  const p = out.playwrightMcp;
  await c.call("browser_navigate", { url });
  const snap = McpClient.text(await c.call("browser_snapshot", {}));
  const alertRef = (snap.match(/button "Open alert" \[ref=(e\d+)\]/) || [])[1];
  await step(p, "browser_click Open alert", async () => McpClient.text(await c.call("browser_click", { element: "Open alert", target: alertRef })), 20000);
  await step(p, "browser_handle_dialog accept", async () => McpClient.text(await c.call("browser_handle_dialog", { accept: true })), 20000);
  await step(p, "browser_evaluate #after", async () => McpClient.text(await c.call("browser_evaluate", { function: "() => document.getElementById('after').textContent" })), 20000);
  await c.call("browser_close", {});
  await c.close();
}
server.close();
writeJson("dialog.json", out);
console.log(JSON.stringify(out, null, 1));
