// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Smoke test: can Stagehand 4.1.0 launch Chrome for Testing 154 headless and load its extension?
import path from "node:path";
import { applyScratchEnvToSelf, startFixtureServer, CHROME, BENCH, now, withTimeout, treeOf, summarize } from "./lib.mjs";
applyScratchEnvToSelf();
const { localBrowser, Stagehand } = await import("./sh/node_modules/@browserbasehq/stagehand/dist/index.mjs");

const { server, base } = await startFixtureServer();
const t0 = now();
let browser, stagehand;
try {
  browser = await withTimeout(localBrowser.launch({
    executablePath: CHROME,
    headless: true,
    userDataDir: path.join(BENCH, "profiles", "sh-smoke"),
    viewport: { width: 1920, height: 1080 },
  }), 60000, "localBrowser.launch");
  const t1 = now();
  stagehand = await withTimeout(Stagehand.create({ browser }), 60000, "Stagehand.create");
  const t2 = now();
  const page = await browser.context.activePage();
  await page.goto(base + "/form.html", { waitUntil: "load" });
  const t3 = now();
  const snap = await page.snapshot();
  const t4 = now();
  console.log(JSON.stringify({ launchMs: t1 - t0, createMs: t2 - t1, gotoMs: t3 - t2, snapshotMs: t4 - t3, treeChars: snap.formattedTree.length }));
  console.log(snap.formattedTree.slice(0, 3000));
  console.log("xpathMap sample", JSON.stringify(Object.entries(snap.xpathMap).slice(0, 5)));
  console.log(JSON.stringify(summarize(treeOf(process.pid))));
} catch (e) {
  console.error("FAILED", e && e.stack || e);
} finally {
  try { await stagehand?.close(); } catch (e) { console.error("stagehand.close", e.message); }
  try { await browser?.close(); } catch (e) { console.error("browser.close", e.message); }
  server.close();
}
