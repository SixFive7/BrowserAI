// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// resume: can a restarted listener take the SAME port and token again, and does the
// page that was open reconnect by itself? This is the restart after an update, in option S.
// Scratch instrument; run under the b2 driver.
//
//   node resume.mjs <chromium|firefox> <sprobe.exe> <outDir> <label> <playwright-core dir> <gapMs>
import fs from "node:fs";
import path from "node:path";
import { spawn } from "node:child_process";
import { pathToFileURL } from "node:url";

const [browserName, sprobe, outDir, label, pwDir, gapText] = process.argv.slice(2);
for (const k of ["LOCALAPPDATA", "TEMP", "TMP", "PLAYWRIGHT_BROWSERS_PATH"]) {
  if (!process.env[k] || !process.env[k].includes("zoomout")) { console.error(`refusing to run: ${k} is not in scratch`); process.exit(2); }
}
const pw = await import(pathToFileURL(path.join(pwDir, "index.mjs")).href);
const t0 = performance.now();
const results = [];
const resultFile = path.join(outDir, label + ".result.txt");
const R = (k, v) => { results.push(`${(performance.now() - t0).toFixed(1).padStart(9)}\t${k}\t${v}`); fs.writeFileSync(resultFile, results.join("\n") + "\n"); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const gap = Number(gapText);

const start = async (tag, extra = []) => {
  const portFile = path.join(outDir, `${label}-${tag}.port`);
  const logPath = path.join(outDir, `${label}-${tag}.sprobe.log`);
  fs.rmSync(portFile, { force: true }); fs.rmSync(portFile + ".error", { force: true });
  const child = spawn(sprobe, [portFile, logPath, ...extra], { windowsHide: true, stdio: ["pipe", "ignore", "ignore"] });
  const began = performance.now();
  while (!fs.existsSync(portFile) && !fs.existsSync(portFile + ".error") && performance.now() - began < 5000) await sleep(1);
  if (fs.existsSync(portFile + ".error")) return { child, error: fs.readFileSync(portFile + ".error", "utf8") };
  const [port, token] = fs.readFileSync(portFile, "utf8").split("\t");
  return { child, port: Number(port), token, logPath };
};
const count = (logPath, marker) => fs.readFileSync(logPath, "utf8").split("\n").filter((l) => l.split("\t")[1] === marker).length;

const browser = browserName === "firefox" ? await pw.firefox.launch({ headless: true }) : await pw.chromium.launch({ channel: "chromium", headless: true });
R("browser", `${browserName} ${browser.version()}`);
const page = await (await browser.newContext()).newPage();

for (let round = 0; round < 3; round++) {
  const a = await start(`a${round}`);
  await page.goto(`http://127.0.0.1:${a.port}/${a.token}/`);
  await page.waitForFunction(() => document.getElementById("link").textContent === "connected");

  // The old process goes, as it does when an update is applied: abruptly, with a page connected.
  const killAt = performance.now();
  a.child.kill();
  await new Promise((r) => a.child.once("exit", r));
  const deadAt = performance.now();
  if (gap > 0) await sleep(gap);

  // The new process asks for the same port and accepts the same token.
  let b, attempts = 0;
  const bindBegan = performance.now();
  do { attempts++; b = await start(`b${round}`, [String(a.port), a.token]); if (b.error) { await sleep(50); } } while (b.error && performance.now() - bindBegan < 15000);
  const boundAt = performance.now();
  if (b.error) { R(`round${round}`, `rebind FAILED for 15 s: ${b.error}`); continue; }

  // Nothing touches the page: its own EventSource has to find the listener again.
  const began = performance.now();
  while (count(b.logPath, "+page") === 0 && performance.now() - began < 30000) await sleep(5);
  const reconnectedAt = performance.now();
  const reconnected = count(b.logPath, "+page") > 0;
  let link = "?", write = "?";
  if (reconnected) {
    await page.waitForFunction(() => document.getElementById("link").textContent === "connected", null, { timeout: 5000 }).catch(() => {});
    link = await page.evaluate(() => document.getElementById("link").textContent);
    write = await page.evaluate(() => window.act()).catch((e) => "THREW " + e.message.split("\n")[0]);
  }
  R(`round${round}`, `kill_to_exit_ms=${(deadAt - killAt).toFixed(1)} gap_ms=${gap} rebind_attempts=${attempts} rebind_ok_ms_after_exit=${(boundAt - deadAt).toFixed(1)} page_reconnected=${reconnected} reconnect_ms_after_rebind=${(reconnectedAt - boundAt).toFixed(0)} reconnect_ms_after_exit=${(reconnectedAt - deadAt).toFixed(0)} link=${link} write_after=${write}`);
  b.child.kill();
  await new Promise((r) => b.child.once("exit", r));
}

await browser.close();
R("done", "0");
process.exit(0);
