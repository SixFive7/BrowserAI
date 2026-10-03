// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import fs from "node:fs";
import path from "node:path";
import { applyScratchEnvToSelf, startFixtureServer, BENCH, OUT, sleep } from "./lib.mjs";
applyScratchEnvToSelf();
const { chromium } = await import("./pw/node_modules/playwright-core/index.mjs");
const { server, base } = await startFixtureServer();
const netlog = path.join(OUT, "pw-fp-netlog.json");
const ctx = await chromium.launchPersistentContext(path.join(BENCH, "profiles", "pw-netlog"), { channel: "chrome-for-testing", headless: true, viewport: { width: 1920, height: 1080 }, args: [`--log-net-log=${netlog}`, "--net-log-capture-mode=Default"] });
const page = ctx.pages()[0] || (await ctx.newPage());
await page.goto(base + "/fp.html");
await sleep(1500);
await page.evaluate(() => document.getElementById("fp").textContent);
await page.locator("#fp").click();
await sleep(8000);
await ctx.close();
server.close();
const s = fs.readFileSync(netlog, "utf8");
const urls = [...s.matchAll(/"url":"([^"]+)"/g)].map((m) => m[1].replace(/\?.*$/, ""));
const counts = {}; for (const u of urls) counts[u] = (counts[u] || 0) + 1;
console.log(JSON.stringify(Object.entries(counts).sort((a, b) => b[1] - a[1]), null, 0));
