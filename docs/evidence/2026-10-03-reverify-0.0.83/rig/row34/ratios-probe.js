// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Re-establishes the Firefox-against-Chromium cost ratios in
// kb/playwright/provisioning-and-timings.md (re-verification row 34): one
// session per family through the product, the same navigation in each, then
// resident set, wall time to first paint, idle CPU over a fixed window with no
// page activity, and profile-directory size on disk.
//
// One family per process run, sequentially, so the idle window of one is not
// measured beside the other's browser. Nothing here touches
// %LocalAppData%\BrowserAI.app.
'use strict';
const { spawn, execFileSync } = require('node:child_process');
const path = require('node:path');
const fs = require('node:fs');
const { Mcp, text } = require('./mcp.js');

const exe = process.argv[2];
const sessionDir = process.argv[3];
const outPath = process.argv[4];
const browser = process.argv[5];
const idleSeconds = Number(process.argv[6] || 30);

const report = { browser, sessionDir, idleSeconds, utc: new Date().toISOString() };
const say = (k, v) => { report[k] = v; console.log(k, '=', typeof v === 'string' ? v.slice(0, 300) : JSON.stringify(v).slice(0, 400)); };

const wait = ms => new Promise(r => setTimeout(r, ms));

// Every process whose executable lives under the browsers root, with its
// working set and CPU so far. Matched on PATH and never on an image name: a
// foreign Firefox and a foreign Chrome are on this machine.
function browserProcesses(browsersRoot) {
  const ps = `Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith('${browsersRoot.replace(/'/g, "''")}', [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { '{0}|{1}|{2}|{3}' -f $_.Id, $_.WorkingSet64, [int64]$_.TotalProcessorTime.TotalMilliseconds, $_.Path }`;
  const out = execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8' });
  return out.split(/\r?\n/).map(l => l.trim()).filter(Boolean).map(l => {
    const [pid, ws, cpu, ...rest] = l.split('|');
    return { pid: Number(pid), workingSet: Number(ws), cpuMs: Number(cpu), exe: rest.join('|') };
  });
}

function treeBytes(dir) {
  let total = 0, files = 0;
  const walk = d => {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const p = path.join(d, e.name);
      if (e.isDirectory()) { walk(p); continue; }
      if (!e.isFile()) continue;
      try { total += fs.statSync(p).size; files++; } catch { /* vanished under us */ }
    }
  };
  try { walk(dir); } catch { /* not there */ }
  return { bytes: total, files };
}

(async () => {
  const browsersRoot = process.argv[7];
  const pageServer = spawn(process.execPath, [path.join(__dirname, 'page-server.js'), '0'],
    { stdio: ['ignore', 'pipe', 'inherit'], windowsHide: true });
  const port = await new Promise(res => pageServer.stdout.once('data', d => res(JSON.parse(d.toString()).port)));
  const url = `http://127.0.0.1:${port}/`;
  say('origin', url);

  const m = new Mcp(exe);
  await m.initialize();

  const init = await m.tool('browserai_init', {
    directory: sessionDir,
    purpose: 'Re-establishing the Firefox-against-Chromium cost ratios, for kb re-verification row 34 on 2026-09-17.',
    browser,
  });
  say('initMs', init.ms);

  // First paint: the first navigate, which is what launches the browser.
  const nav1 = await m.tool('browser_navigate', { session: sessionDir, why: 'First navigation of the session, which is the wall time to first paint this row compares.', url });
  say('firstNavigateMs', nav1.ms);

  // A second navigation to the same origin, with the browser already up.
  const nav2 = await m.tool('browser_navigate', { session: sessionDir, why: 'A second navigation with the browser already up, so the launch half of the first is visible.', url });
  say('secondNavigateMs', nav2.ms);

  await wait(2000);
  const first = browserProcesses(browsersRoot);
  say('processes', first.length);
  say('workingSetBytes', first.reduce((a, p) => a + p.workingSet, 0));

  // Idle CPU over a fixed window, with no page activity at all.
  const t0 = process.hrtime.bigint();
  const cpu0 = first.reduce((a, p) => a + p.cpuMs, 0);
  await wait(idleSeconds * 1000);
  const second = browserProcesses(browsersRoot);
  const elapsedMs = Number(process.hrtime.bigint() - t0) / 1e6;
  const cpu1 = second.reduce((a, p) => a + p.cpuMs, 0);

  say('idleWindowMs', Math.round(elapsedMs));
  say('idleCpuMs', Math.round((cpu1 - cpu0) * 1000) / 1000);
  say('idleCpuPercentOfOneCore', Math.round(((cpu1 - cpu0) / elapsedMs) * 100 * 1000) / 1000);
  say('processesAfterIdle', second.length);
  say('workingSetAfterIdleBytes', second.reduce((a, p) => a + p.workingSet, 0));

  const profile = treeBytes(path.join(sessionDir, 'profile'));
  say('profileBytes', profile.bytes);
  say('profileFiles', profile.files);

  say('destroy', text(await m.tool('browserai_destroy', { directory: sessionDir, why: 'The probe is finished; leave nothing in the session index.' })).slice(0, 200));

  m.close();
  pageServer.kill();
  report.stderrTail = m.stderr.slice(-1200);
  fs.writeFileSync(outPath, JSON.stringify(report, null, 2), 'utf8');
  await wait(600);
  process.exit(0);
})().catch(err => {
  report.error = String((err && err.stack) || err);
  try { fs.writeFileSync(outPath, JSON.stringify(report, null, 2), 'utf8'); } catch { /* nothing left to do */ }
  console.error(report.error);
  process.exit(1);
});
