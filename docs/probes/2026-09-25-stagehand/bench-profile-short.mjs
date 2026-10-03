// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import path from "node:path";
import { applyScratchEnvToSelf, startFixtureServer, now, writeJson, BENCH, CHROME, sleep } from "./lib.mjs";
applyScratchEnvToSelf();
const { localBrowser, Stagehand } = await import("./sh/node_modules/@browserbasehq/stagehand/dist/index.mjs");
const { server, base } = await startFixtureServer();
const url = base + "/blank.html";
const out = [];
const dir = path.join(BENCH, "profiles", "sh-short-reused");
for (let i = 0; i < 6; i++) {
  const t0 = now();
  const browser = await localBrowser.launch({ executablePath: CHROME, headless: true, userDataDir: dir, viewport: { width: 1920, height: 1080 } });
  const t1 = now();
  const stagehand = await Stagehand.create({ browser, logging: { level: "error" } });
  const t2 = now();
  const page = await browser.context.activePage();
  await page.goto(url, { waitUntil: "load" });
  const t3 = now();
  await stagehand.close();
  await browser.close();
  const t4 = now();
  out.push({ launchMs: Math.round(t1 - t0), createMs: Math.round(t2 - t1), gotoMs: Math.round(t3 - t2), closeMs: Math.round(t4 - t3) });
  process.stderr.write(JSON.stringify(out.at(-1)) + "\n");
  await sleep(300);
}
server.close();
writeJson("profile-short.json", out);
