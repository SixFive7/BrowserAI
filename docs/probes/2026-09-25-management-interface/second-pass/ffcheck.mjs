// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// ffcheck: the same browser checks as stest, in Playwright's Firefox build, headless.
// Scratch instrument. Run under the b2 driver so it sits on a private desktop in a kill-on-close job.
//
//   node ffcheck.mjs <sprobe.exe> <outDir> <label> <playwright-core dir>
import fs from "node:fs";
import path from "node:path";
import net from "node:net";
import { spawn } from "node:child_process";
import { pathToFileURL } from "node:url";

const [sprobe, outDir, label, pwDir] = process.argv.slice(2);
for (const k of ["LOCALAPPDATA", "TEMP", "TMP", "PLAYWRIGHT_BROWSERS_PATH"]) {
  if (!process.env[k] || !process.env[k].includes("zoomout")) { console.error(`refusing to run: ${k} is not in scratch`); process.exit(2); }
}
const { firefox } = await import(pathToFileURL(path.join(pwDir, "index.mjs")).href);

const t0 = performance.now();
const results = [];
const resultFile = path.join(outDir, label + ".result.txt");
const R = (k, v) => { results.push(`${(performance.now() - t0).toFixed(1).padStart(9)}\t${k}\t${v}`); fs.writeFileSync(resultFile, results.join("\n") + "\n"); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const portFile = path.join(outDir, label + ".port");
const logPath = path.join(outDir, label + ".sprobe.log");
fs.rmSync(portFile, { force: true });
const server = spawn(sprobe, [portFile, logPath], { windowsHide: true, stdio: ["pipe", "ignore", "ignore"] });
while (!fs.existsSync(portFile)) await sleep(5);
const [portText, token, otherText] = fs.readFileSync(portFile, "utf8").split("\t");
const port = Number(portText), other = Number(otherText);
const origin = `http://127.0.0.1:${port}`;
const url = `${origin}/${token}/`;
const mask = (s) => String(s).split(token).join("<token>");
const log = () => fs.readFileSync(logPath, "utf8").split("\n").filter(Boolean);
const count = (marker) => log().filter((l) => l.split("\t")[1] === marker).length;
const waitLog = async (cond, ms = 10000) => { const s = performance.now(); while (performance.now() - s < ms) { if (cond()) return true; await sleep(1); } return false; };
const lastDeny = () => mask(log().filter((l) => l.includes("deny\t")).slice(-1).map((l) => l.trim().split("\t").slice(1).join(" ")).join(""));
const raw = (request) => new Promise((resolve) => { const s = net.connect(port, "127.0.0.1", () => s.write(request)); let d = ""; s.on("data", (c) => (d += c)); s.on("end", () => resolve(d)); s.on("error", () => resolve(d)); });

const browser = await firefox.launch({ headless: true, firefoxUserPrefs: { "network.dns.localDomains": "attacker.example" } });
R("browser", "Firefox " + browser.version());
const context = await browser.newContext();

// B1
let plus = count("+page");
const page1 = await context.newPage();
await page1.goto(url);
R("B1_connected", await waitLog(() => count("+page") > plus));
await page1.waitForFunction(() => document.getElementById("link").textContent === "connected");
R("B1_write_status", await page1.evaluate(() => window.act()));
await page1.waitForFunction(() => document.getElementById("state").textContent.includes('"counter":1'));
R("B1_state_pushed", await page1.evaluate(() => document.getElementById("state").textContent));
R("B1_cookies_and_storage", await page1.evaluate(() => JSON.stringify({ cookie: document.cookie, local: localStorage.length, session: sessionStorage.length })));

// B2
const gaps = [];
for (let i = 0; i < 10; i++) {
  const minus = count("-page"); plus = count("+page");
  await page1.reload();
  if (!(await waitLog(() => count("-page") > minus && count("+page") > plus))) { R("B2_reload", `run ${i}: no reconnect seen`); continue; }
  const lines = log();
  const at = (marker, nth) => Number(lines.filter((l) => l.split("\t")[1] === marker)[nth].split("\t")[0]);
  gaps.push(at("+page", plus) - at("-page", minus));
  await sleep(150);
}
gaps.sort((a, b) => a - b);
R("B2_reload_gap_ms", gaps.length ? `n=${gaps.length} min=${gaps[0].toFixed(1)} median=${gaps[gaps.length >> 1].toFixed(1)} max=${gaps[gaps.length - 1].toFixed(1)}` : "none");

// B3
plus = count("+page");
const page2 = await context.newPage();
await page2.goto(url, { timeout: 8000 }).catch(async (e) => {
  const state = await page2.evaluate(() => JSON.stringify({ ready: document.readyState, link: document.getElementById("link")?.textContent, state: document.getElementById("state")?.textContent, scripts: document.scripts.length, sheets: document.styleSheets.length })).catch((x) => "evaluate threw " + String(x.message).slice(0, 120));
  R("B3_goto_did_not_resolve", String(e.message).slice(0, 120).replace(/\s+/g, " ") + " || page: " + state);
});
R("B3_second_tab_connected", await waitLog(() => count("+page") > plus));
const minus = count("-page");
const c0 = performance.now();
await page1.close();
R("B3_tab_close_seen_by_the_listener_ms", (await waitLog(() => count("-page") > minus)) ? (performance.now() - c0).toFixed(1) : "not seen");

// B4: rebinding with the token leaked
const rebind = await context.newPage();
const response = await rebind.goto(`http://attacker.example:${port}/${token}/`).catch((e) => "ERR " + e.message.split("\n")[0]);
R("B4_rebinding_navigation", typeof response === "string" ? mask(response) : `status ${response.status()}`);
R("B4_listener", lastDeny());

// B5: another site that knows the whole URL
const otherPage = await context.newPage();
await otherPage.goto(`http://attacker.example:${other}/`);
R("B5_other_site_origin", await otherPage.evaluate(() => location.origin));
const ev = (fn, arg) => otherPage.evaluate(fn, arg).catch((e) => "THREW " + e.message.split("\n")[0]);
R("B5a_cross_site_post_text_plain", (await ev((u) => fetch(u + "act", { method: "POST", mode: "no-cors", headers: { "Content-Type": "text/plain" }, body: "x" }).then((r) => r.type + " " + r.status, (e) => "ERR " + e.message), url)) + " || listener: " + lastDeny());
R("B5b_cross_site_get_no_cors", (await ev((u) => fetch(u + "state", { mode: "no-cors" }).then((r) => r.type + " " + r.status, (e) => "ERR " + e.message), url)) + " || listener: " + lastDeny());
R("B5c_cross_site_get_cors", (await ev((u) => fetch(u + "state").then((r) => r.type + " " + r.status, (e) => "ERR " + e.message), url)) + " || listener: " + lastDeny());
R("B5d_cross_site_json_post", (await ev((u) => fetch(u + "act", { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" }).then((r) => r.type + " " + r.status, (e) => "ERR " + e.message), url)) + " || listener: " + lastDeny());
R("B5e_iframe", (await ev((u) => new Promise((res) => { const f = document.createElement("iframe"); f.onload = () => { let d; try { d = f.contentDocument ? f.contentDocument.title + "/" + f.contentDocument.body.innerText.length : "null document"; } catch (e) { d = "threw " + e.name; } res(d); }; f.src = u; document.body.appendChild(f); setTimeout(() => res("no load event"), 4000); }), url)) + " || listener: " + lastDeny());
R("B5f_form_post", (await ev((u) => new Promise((res) => { const f = document.createElement("iframe"); f.name = "sink"; document.body.appendChild(f); const form = document.createElement("form"); form.method = "post"; form.action = u + "act"; form.target = "sink"; document.body.appendChild(form); form.submit(); setTimeout(() => res("submitted"), 800); }), url)) + " || listener: " + lastDeny());
R("B5g_websocket", (await ev(([p, t]) => new Promise((res) => { const w = new WebSocket(`ws://127.0.0.1:${p}/${t}/events`); w.onopen = () => res("OPEN"); w.onerror = () => res("error"); setTimeout(() => res("timeout"), 4000); }), [port, token])) + " || listener: " + lastDeny());
R("B5_counter_after_all_of_that", (await raw(`GET /${token}/state HTTP/1.1\r\nHost: 127.0.0.1:${port}\r\nConnection: close\r\n\r\n`)).split("\r\n\r\n")[1]);

// B6: can the page close its own tab?
const page3 = await context.newPage();
await page3.goto(url);
R("B6_history_length", await page3.evaluate(() => String(history.length)));
let closed = false;
page3.on("close", () => { closed = true; });
await page3.evaluate(() => { window.close(); }).catch(() => {});
await sleep(800);
R("B6_window_close_closed_the_tab", closed);

for (const line of [...new Set(log().filter((l) => l.includes("admit\t")).map((l) => l.trim().split("\t").slice(1).join(" ")))].slice(0, 12)) R("admitted", line);

await browser.close();
await raw(`POST /${token}/quit HTTP/1.1\r\nHost: 127.0.0.1:${port}\r\nOrigin: ${origin}\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}`);
server.stdin.end();
R("done", "0");
process.exit(0);
