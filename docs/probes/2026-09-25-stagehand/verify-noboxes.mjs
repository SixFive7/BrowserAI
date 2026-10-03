// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import { countTokens } from "./tok/node_modules/gpt-tokenizer/esm/encoding/o200k_base.js";
import { startPwMcp } from "./pwmcp.mjs";
import { McpClient } from "./mcpclient.mjs";
import { startFixtureServer } from "./lib.mjs";
const { server, base } = await startFixtureServer();
const on = await startPwMcp("vb-on", { boxes: true }), off = await startPwMcp("vb-off", { boxes: false });
for (const url of [base + "/form.html", base + "/table.html", base + "/iframes.html"]) {
  const bodies = [];
  for (const c of [on, off]) { await c.call("browser_navigate", { url }); const t = McpClient.text(await c.call("browser_snapshot", {})); bodies.push(t.match(/```yaml\n([\s\S]*?)\n```/)[1]); }
  const stripped = bodies[0].replace(/ \[box=[^\]]*\]/g, "");
  console.log(url.split("/").pop(), "boxesOn", countTokens(bodies[0]), "stripped", countTokens(stripped), "boxesOff", countTokens(bodies[1]), "identical", stripped === bodies[1]);
}
for (const c of [on, off]) { await c.call("browser_close", {}); await c.close(); }
server.close();
