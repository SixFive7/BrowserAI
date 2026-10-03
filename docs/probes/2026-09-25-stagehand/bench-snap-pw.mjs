// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Snapshot size/time and memory for @playwright/mcp 0.0.82 configured like BrowserAI. Scratch only.
import { countTokens } from "./tok/node_modules/gpt-tokenizer/esm/encoding/o200k_base.js";
import { startPwMcp } from "./pwmcp.mjs";
import { McpClient } from "./mcpclient.mjs";
import { startFixtureServer, now, treeOf, summarize, writeJson, sleep } from "./lib.mjs";
import { pageList } from "./pages.mjs";

const round = process.argv[2] || "A";
const { server, base } = await startFixtureServer();
const c = await startPwMcp(`snap-${round}`);
const results = [];
const memory = {};
try {
  for (const [name, url, reps] of pageList(base)) {
    for (let r = 0; r < reps; r++) {
      const t0 = now();
      const nav = await c.call("browser_navigate", { url });
      const t1 = now();
      const snap = await c.call("browser_snapshot", {});
      const t2 = now();
      const navText = McpClient.text(nav);
      const text = McpClient.text(snap);
      const m = text.match(/```yaml\n([\s\S]*?)\n```/);
      const body = m ? m[1] : text;
      const bodyNoBoxes = body.replace(/ \[box=[^\]]*\]/g, "");
      results.push({
        page: name, rep: r, navMs: +(t1 - t0).toFixed(1), snapMs: +(t2 - t1).toFixed(1),
        navResponseChars: navText.length, navResponseTokens: countTokens(navText),
        responseChars: text.length, responseTokens: countTokens(text),
        bodyChars: body.length, bodyTokens: countTokens(body),
        bodyNoBoxesChars: bodyNoBoxes.length, bodyNoBoxesTokens: countTokens(bodyNoBoxes),
        refs: (body.match(/\[ref=e\d+\]/g) || []).length,
        isError: !!snap.result?.isError || !!nav.result?.isError,
      });
      if (r === 0) {
        await sleep(500);
        memory[name] = summarize(treeOf(c.pid));
        writeJson(`snap-pw-${round}-${name}-sample.txt.json`, { navText, text });
      }
      process.stderr.write(`${name} ${r} nav ${(t1 - t0).toFixed(0)} snap ${(t2 - t1).toFixed(0)} chars ${text.length}\n`);
    }
  }
  await c.call("browser_close", {});
} catch (e) {
  console.error("FAILED", e.stack || e, c.stderr.slice(-2000));
} finally {
  await c.close();
  server.close();
}
writeJson(`snap-pw-${round}.json`, { tool: "playwright-mcp-0.0.82", round, results, memory });
console.log("done", results.length);
