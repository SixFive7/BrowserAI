// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A child host process that launches Stagehand + CfT headless, loads a page, reports, and closes.
import path from "node:path";
import { applyScratchEnvToSelf, now, CHROME, BENCH } from "./lib.mjs";
const tStart = now();
applyScratchEnvToSelf();
const { localBrowser, Stagehand } = await import("./sh/node_modules/@browserbasehq/stagehand/dist/index.mjs");
const tImport = now();
const [url, profile] = process.argv.slice(2);
const browser = await localBrowser.launch({ executablePath: CHROME, headless: true, userDataDir: path.join(BENCH, "profiles", profile), viewport: { width: 1920, height: 1080 } });
const tLaunch = now();
const stagehand = await Stagehand.create({ browser, logging: { level: "error" } });
const tCreate = now();
const page = await browser.context.activePage();
await page.goto(url, { waitUntil: "load" });
const tGoto = now();
process.stdout.write(JSON.stringify({ importMs: tImport - tStart, launchMs: tLaunch - tImport, createMs: tCreate - tLaunch, gotoMs: tGoto - tCreate, inProcessTotalMs: tGoto - tStart }) + "\n");
await stagehand.close();
await browser.close();
process.exit(0);
