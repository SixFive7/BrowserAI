// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import { startPwMcp } from "./pwmcp.mjs";
import { McpClient } from "./mcpclient.mjs";
import { startFixtureServer, now, treeOf, summarize } from "./lib.mjs";

const { server, base } = await startFixtureServer();
const t0 = now();
const c = await startPwMcp("smoke");
const t1 = now();
try {
  const list = await c.request("tools/list", {});
  console.log("tools:", list.result.tools.length);
  const nav = await c.call("browser_navigate", { url: base + "/form.html" });
  const t2 = now();
  const snap = await c.call("browser_snapshot", {});
  const t3 = now();
  const navText = McpClient.text(nav), snapText = McpClient.text(snap);
  console.log(JSON.stringify({ initMs: t1 - t0, firstNavigateMs: t2 - t1, snapshotMs: t3 - t2, navChars: navText.length, snapChars: snapText.length }));
  console.log("--- navigate response\n" + navText.slice(0, 1500));
  console.log("--- snapshot response\n" + snapText.slice(0, 4000));
  console.log(JSON.stringify(summarize(treeOf(c.pid))));
  await c.call("browser_close", {});
} catch (e) {
  console.error("FAILED", e.stack || e);
  console.error(c.stderr.slice(-3000));
} finally {
  await c.close();
  server.close();
}
