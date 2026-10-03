// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Stagehand on a fresh vs a reused (persistent) profile: create() time, close() time, and the exit_type
// Chrome records. Playwright's persistent context as the control. Scratch only.
import fs from "node:fs";
import path from "node:path";
import { applyScratchEnvToSelf, startFixtureServer, now, writeJson, BENCH, CHROME, sleep } from "./lib.mjs";
applyScratchEnvToSelf();
const { chromium } = await import("./pw/node_modules/playwright-core/index.mjs");
const { localBrowser, Stagehand } = await import("./sh/node_modules/@browserbasehq/stagehand/dist/index.mjs");
const { server, base } = await startFixtureServer();
const url = base + "/form.html";

function exitType(dir) {
  const f = path.join(dir, "Default", "Preferences");
  if (!fs.existsSync(f)) return "no-Preferences";
  try { return JSON.parse(fs.readFileSync(f, "utf8")).profile?.exit_type ?? "unset"; } catch { return "unreadable"; }
}
async function shRun(dir) {
  const t0 = now();
  const browser = await localBrowser.launch({ executablePath: CHROME, headless: true, userDataDir: dir, viewport: { width: 1920, height: 1080 } });
  const t1 = now();
  const stagehand = await Stagehand.create({ browser, logging: { level: "error" } });
  const t2 = now();
  const page = await browser.context.activePage();
  await page.goto(url, { waitUntil: "load" });
  await page.locator("#email").fill("persist@example.com");
  await page.evaluate(() => { localStorage.setItem("k", "v" + Date.now()); document.cookie = "c=1; max-age=3600"; });
  await sleep(11000); // let Chrome's 10 s pref/cookie commit interval pass once
  const t3 = now();
  await stagehand.close();
  const t4 = now();
  await browser.close();
  const t5 = now();
  await sleep(500);
  return { launchMs: t1 - t0, createMs: t2 - t1, stagehandCloseMs: t4 - t3, browserCloseMs: t5 - t4, exitType: exitType(dir) };
}
async function pwRun(dir) {
  const t0 = now();
  const ctx = await chromium.launchPersistentContext(dir, { channel: "chrome-for-testing", headless: true, viewport: { width: 1920, height: 1080 } });
  const t1 = now();
  const page = ctx.pages()[0] || (await ctx.newPage());
  await page.goto(url, { waitUntil: "load" });
  await page.evaluate(() => { localStorage.setItem("k", "v" + Date.now()); document.cookie = "c=1; max-age=3600"; });
  await sleep(11000);
  const t3 = now();
  await ctx.close();
  const t4 = now();
  await sleep(500);
  return { launchMs: t1 - t0, closeMs: t4 - t3, exitType: exitType(dir) };
}
const out = { shFresh: [], shReused: [], pwReused: [] };
for (let i = 0; i < 3; i++) {
  const fresh = path.join(BENCH, "profiles", `sh-fresh-${Date.now()}-${i}`);
  out.shFresh.push(await shRun(fresh)); process.stderr.write("shFresh " + JSON.stringify(out.shFresh.at(-1)) + "\n");
}
const reused = path.join(BENCH, "profiles", "sh-reused");
for (let i = 0; i < 4; i++) { out.shReused.push(await shRun(reused)); process.stderr.write("shReused " + JSON.stringify(out.shReused.at(-1)) + "\n"); }
const pwReused = path.join(BENCH, "profiles", "pw-reused");
for (let i = 0; i < 3; i++) { out.pwReused.push(await pwRun(pwReused)); process.stderr.write("pwReused " + JSON.stringify(out.pwReused.at(-1)) + "\n"); }
server.close();
writeJson("profile.json", out);
console.log(JSON.stringify(out, null, 1));
