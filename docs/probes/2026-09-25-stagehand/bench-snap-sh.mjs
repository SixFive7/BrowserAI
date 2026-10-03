// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Snapshot size/time and memory for Stagehand 4.1.0 (the facade's snapshot tool returns formattedTree). Scratch only.
import path from "node:path";
import { countTokens } from "./tok/node_modules/gpt-tokenizer/esm/encoding/o200k_base.js";
import { applyScratchEnvToSelf, startFixtureServer, now, treeOf, summarize, writeJson, sleep, CHROME, BENCH, withTimeout } from "./lib.mjs";
import { pageList } from "./pages.mjs";
applyScratchEnvToSelf();
const { localBrowser, Stagehand } = await import("./sh/node_modules/@browserbasehq/stagehand/dist/index.mjs");

const round = process.argv[2] || "A";
const { server, base } = await startFixtureServer();
const results = [];
const memory = {};
let browser, stagehand;
try {
  browser = await localBrowser.launch({ executablePath: CHROME, headless: true, userDataDir: path.join(BENCH, "profiles", `sh-snap-${round}`), viewport: { width: 1920, height: 1080 } });
  stagehand = await Stagehand.create({ browser, logging: { level: "error" } });
  const page = await browser.context.activePage();
  for (const [name, url, reps] of pageList(base)) {
    for (let r = 0; r < reps; r++) {
      const t0 = now();
      await withTimeout(page.goto(url, { waitUntil: "load", timeout: 60000 }), 70000, "goto");
      const t1 = now();
      const snap = await withTimeout(page.snapshot({ includeIframes: true }), 60000, "snapshot");
      const t2 = now();
      const tree = snap.formattedTree;
      results.push({
        page: name, rep: r, navMs: +(t1 - t0).toFixed(1), snapMs: +(t2 - t1).toFixed(1),
        bodyChars: tree.length, bodyTokens: countTokens(tree),
        ids: Object.keys(snap.xpathMap || {}).length,
      });
      if (r === 0) {
        await sleep(500);
        memory[name] = summarize(treeOf(process.pid));
        writeJson(`snap-sh-${round}-${name}-sample.txt.json`, { tree });
      }
      process.stderr.write(`${name} ${r} nav ${(t1 - t0).toFixed(0)} snap ${(t2 - t1).toFixed(0)} chars ${tree.length}\n`);
    }
  }
} catch (e) {
  console.error("FAILED", e.stack || e);
} finally {
  try { await stagehand?.close(); } catch {}
  try { await browser?.close(); } catch {}
  server.close();
}
writeJson(`snap-sh-${round}.json`, { tool: "stagehand-4.1.0", round, results, memory });
console.log("done", results.length);
