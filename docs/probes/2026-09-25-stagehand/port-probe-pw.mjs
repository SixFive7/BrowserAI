// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import path from "node:path";
import { execFileSync } from "node:child_process";
import { startPwMcp } from "./pwmcp.mjs";
import { BENCH, writeJson, treeOf } from "./lib.mjs";
const c = await startPwMcp("port");
await c.call("browser_navigate", { url: "about:blank" });
const chromeKids = treeOf(c.pid).filter((p) => p.ParentProcessId === c.pid && p.Name === "chrome.exe");
const out = {};
for (const k of chromeKids) {
  out[k.ProcessId] = JSON.parse(execFileSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", path.join(BENCH, "port-probe.ps1"), "-ParentPid", String(c.pid)], { windowsHide: true }).toString());
}
await c.call("browser_close", {}); await c.close();
writeJson("port-probe-pw.json", out);
console.log(JSON.stringify(out, null, 1));
