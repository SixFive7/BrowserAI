// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import path from "node:path";
import { execFileSync } from "node:child_process";
import { applyScratchEnvToSelf, BENCH, CHROME, writeJson, sleep } from "./lib.mjs";
applyScratchEnvToSelf();
const { localBrowser, Stagehand } = await import("./sh/node_modules/@browserbasehq/stagehand/dist/index.mjs");
const browser = await localBrowser.launch({ executablePath: CHROME, headless: true, userDataDir: path.join(BENCH, "profiles", "sh-port"), viewport: { width: 1280, height: 800 } });
const out = {};
try {
  const stagehand = await Stagehand.create({ browser, logging: { level: "error" } });
  await sleep(500);
  out.listener = JSON.parse(execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path.join(BENCH, "port-probe.ps1"), "-ParentPid", String(process.pid)], { windowsHide: true }).toString());
  const port = out.listener.port;
  out.fromAnotherProcess = JSON.parse(execFileSync("curl.exe", ["-s", "-m", "10", `http://127.0.0.1:${port}/json/version`], { windowsHide: true }).toString());
  out.targetsVisibleToAnotherProcess = JSON.parse(execFileSync("curl.exe", ["-s", "-m", "10", `http://127.0.0.1:${port}/json/list`], { windowsHide: true }).toString()).map((t) => ({ type: t.type, url: String(t.url).slice(0, 90) }));
  await stagehand.close();
} catch (e) { out.error = String(e.stack || e); }
finally { await browser.close(); }
writeJson("port-probe.json", out);
console.log(JSON.stringify(out, null, 1));
