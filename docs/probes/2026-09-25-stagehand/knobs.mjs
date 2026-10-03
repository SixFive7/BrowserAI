// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Two knobs inside the current stack: per-call boxes on browser_snapshot, and timeouts.settle.
import fs from "node:fs";
import { countTokens } from "./tok/node_modules/gpt-tokenizer/esm/encoding/o200k_base.js";
import { writeConfig } from "./pwmcp.mjs";
import { McpClient } from "./mcpclient.mjs";
import { startFixtureServer, now, stats, scratchEnv, BENCH, writeJson } from "./lib.mjs";
import path from "node:path";
const { server, base } = await startFixtureServer();
const cli = path.join(BENCH, "pw", "node_modules", "@playwright", "mcp", "cli.js");
async function start(tag, settle) {
  const { file } = writeConfig(tag, { boxes: true });
  const cfg = JSON.parse(fs.readFileSync(file, "utf8"));
  if (settle !== undefined) cfg.timeouts = { settle };
  fs.writeFileSync(file, JSON.stringify(cfg, null, 1));
  const c = new McpClient(process.execPath, [cli, "--config", file], scratchEnv(), BENCH);
  await c.initialize();
  return c;
}
const out = {};
{ // per-call boxes override
  const c = await start("knob-boxes");
  await c.call("browser_navigate", { url: base + "/table.html" });
  const a = McpClient.text(await c.call("browser_snapshot", {}));
  const b = McpClient.text(await c.call("browser_snapshot", { boxes: false }));
  out.perCallBoxes = { configBoxesOn_default: countTokens(a), configBoxesOn_callBoxesFalse: countTokens(b), boxesPresentInOverride: /\[box=/.test(b) };
  await c.call("browser_close", {}); await c.close();
}
for (const settle of [undefined, 100, 0]) {
  const c = await start("knob-settle-" + (settle ?? "default"), settle);
  await c.call("browser_navigate", { url: base + "/form.html" });
  const snap = McpClient.text(await c.call("browser_snapshot", {}));
  const ref = (snap.match(/button "Count" \[ref=(e\d+)\]/) || [])[1];
  const t = [];
  for (let i = 0; i < 15; i++) { const s = now(); const r = await c.call("browser_click", { element: "Count", target: ref }); t.push(now() - s); if (r.result?.isError) throw new Error(McpClient.text(r)); }
  const count = McpClient.text(await c.call("browser_evaluate", { function: "() => document.getElementById('count').textContent" }));
  out["settle_" + (settle ?? "default500")] = { click: stats(t), count: count.slice(0, 40) };
  await c.call("browser_close", {}); await c.close();
}
server.close();
writeJson("knobs.json", out);
console.log(JSON.stringify(out, null, 1));
