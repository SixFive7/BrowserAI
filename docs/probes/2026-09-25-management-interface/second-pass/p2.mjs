// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// p2: what option P2 would do. The bundled node runs playwright-core, which starts the
// browser as an app window and serves the page through page.route, with no port.
// Scratch instrument; run under the b2 driver (private desktop, kill-on-close job).
//
//   node p2.mjs <playwright-core dir> <userDataDir> <outDir> <label> <holdMs>
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const [pwDir, userDataDir, outDir, label, holdText] = process.argv.slice(2);
for (const k of ["LOCALAPPDATA", "TEMP", "TMP", "PLAYWRIGHT_BROWSERS_PATH", "PWTEST_SERVER_REGISTRY"]) {
  if (!process.env[k] || !process.env[k].includes("zoomout")) { console.error(`refusing to run: ${k} is not in scratch`); process.exit(2); }
}
const results = [];
const resultFile = path.join(outDir, label + ".result.txt");
const R = (k, v) => { results.push(`${performance.now().toFixed(1).padStart(9)}\t${k}\t${v}`); fs.writeFileSync(resultFile, results.join("\n") + "\n"); };

R("node", process.version + " main reached");
const { chromium } = await import(pathToFileURL(path.join(pwDir, "index.mjs")).href);
R("playwright_core_loaded", "");

const Origin = "https://app.browserai.invalid";
const html = `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>BrowserAI</title><link rel="stylesheet" href="/app.css"><script src="/app.js" defer></script></head><body><h1>BrowserAI</h1><p id="state">loading</p></body></html>`;
const css = "html{color-scheme:light dark;font:14px system-ui}body{margin:24px}";
const js = `const send = (o) => window.browseraiHost(JSON.stringify(o));
window.hostPing = (n) => { send({ pong: n }); return n; };
document.getElementById('state').textContent = 'ready';
send({ ready: true, inner: [innerWidth, innerHeight], outer: [outerWidth, outerHeight], ua: navigator.userAgent });`;

const launchAt = performance.now();
const context = await chromium.launchPersistentContext(userDataDir, {
  headless: false,
  viewport: null,
  ignoreDefaultArgs: ["--enable-automation"],
  args: ["--app=data:text/html,<title>BrowserAI</title>", "--window-size=980,720", "--window-position=60,60"],
});
R("context_ms_after_launch", (performance.now() - launchAt).toFixed(1));
const page = context.pages()[0] ?? (await context.newPage());
let ready;
const isReady = new Promise((resolve) => { ready = resolve; });
let pongs = 0;
await page.exposeBinding("browseraiHost", (_source, payload) => { if (payload.includes('"ready"')) ready(payload); else pongs++; });
await page.route("**/*", (route) => {
  const url = route.request().url();
  if (url === Origin + "/") return route.fulfill({ status: 200, contentType: "text/html; charset=utf-8", body: html });
  if (url === Origin + "/app.js") return route.fulfill({ status: 200, contentType: "text/javascript", body: js });
  if (url === Origin + "/app.css") return route.fulfill({ status: 200, contentType: "text/css", body: css });
  return route.abort();
});
await page.goto(Origin + "/");
const said = await isReady;
R("page_ready_ms_after_launch", (performance.now() - launchAt).toFixed(1));
R("page_ready_ms_after_node_start", performance.now().toFixed(1));
R("page_said", said);
fs.writeFileSync(path.join(outDir, label + ".ready"), performance.now().toFixed(1));

const trips = [];
for (let i = 0; i < 40; i++) { const t = performance.now(); await page.evaluate((n) => window.hostPing(n), i); trips.push(performance.now() - t); }
trips.sort((a, b) => a - b);
R("round_trip_ms", `p50=${trips[20].toFixed(2)} min=${trips[0].toFixed(2)} max=${trips[39].toFixed(2)} pongs=${pongs}`);

await new Promise((r) => setTimeout(r, Number(holdText)));
const closeAt = performance.now();
await context.close();
R("closed_ms", (performance.now() - closeAt).toFixed(1));
R("done", "0");
process.exit(0);
