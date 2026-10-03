// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch probe: does a headless Playwright Firefox launch hang when Firefox
// starts in safe mode? Safe mode is forced with Firefox's own
// MOZ_SAFE_MODE_RESTART=1, so no keyboard is involved.
// Usage: node ff-safemode.cjs <playwright-core dir> <firefox.exe> <arm> <outdir>
// Arms: control-persistent, forced-persistent, forced-persistent-keyoff,
//       control-nonpersistent, forced-nonpersistent
'use strict';
const fs = require('fs');
const path = require('path');
const http = require('http');
const cp = require('child_process');

const [PW, EXE, ARM, OUT] = process.argv.slice(2);
fs.mkdirSync(OUT, { recursive: true });
const t0 = Date.now();
const ms = () => Date.now() - t0;
const logFile = path.join(OUT, `${ARM}.log`);
const w = (s) => fs.appendFileSync(logFile, `+${String(ms()).padStart(6)} ms ${s}\n`);

let firefoxPid = 0;
const origSpawn = cp.spawn;
cp.spawn = function (command, argv) {
  const child = origSpawn.apply(this, arguments);
  if (/firefox\.exe$/i.test(String(command))) {
    firefoxPid = child.pid || 0;
    w(`SPAWN pid=${firefoxPid} args=${(argv || []).join(' ')}`);
  }
  return child;
};
const { firefox } = require(PW);

function tree(rootPid) {
  if (!rootPid) return [];
  const ps = `$r=${rootPid}; $all=@(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,CommandLine); $set=@{}; $set[[int]$r]=$true; $c=$true; while($c){$c=$false; foreach($p in $all){ if($set.ContainsKey([int]$p.ParentProcessId) -and -not $set.ContainsKey([int]$p.ProcessId)){ $set[[int]$p.ProcessId]=$true; $c=$true } } }; $all | Where-Object { $set.ContainsKey([int]$_.ProcessId) } | ForEach-Object { "$($_.ProcessId)|$($_.ParentProcessId)|$($_.CommandLine)" }`;
  try {
    return cp.execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8', windowsHide: true, timeout: 60000 }).split(/\r?\n/).filter(Boolean);
  } catch (e) { return ['ERROR ' + String(e.message || e).slice(0, 300)]; }
}
function summary(lines) {
  return {
    processes: lines.filter((l) => !l.startsWith('ERROR')).length,
    contentProcesses: lines.filter((l) => l.includes('-contentproc')).length,
    safeModeProcesses: lines.filter((l) => / -safeMode( |$)/.test(l)).length,
  };
}
const race = (p, msBound, label) => Promise.race([
  p.then((v) => ({ ok: true, v })),
  new Promise((r) => setTimeout(() => r({ ok: false, timedOut: true, label }), msBound)),
]);

(async () => {
  const server = http.createServer((req, res) => { res.writeHead(200, { 'content-type': 'text/html' }); res.end('<title>probe</title><p>hello</p>'); });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  const url = `http://127.0.0.1:${server.address().port}/`;
  const forced = ARM.startsWith('forced');
  const result0 = {};
  // The inherited environment on this machine carries MOZ_DISABLE_SAFE_MODE_KEY=1
  // at user level, so it is removed here unless the arm asks for it.
  const env = { ...process.env };
  result0.inheritedKeyVariable = env.MOZ_DISABLE_SAFE_MODE_KEY ?? null;
  delete env.MOZ_DISABLE_SAFE_MODE_KEY;
  delete env.MOZ_SAFE_MODE_RESTART;
  if (forced) env.MOZ_SAFE_MODE_RESTART = '1';
  if (ARM.endsWith('keyoff')) env.MOZ_DISABLE_SAFE_MODE_KEY = '1';
  const persistent = ARM.includes('-persistent');
  const launchTimeout = forced ? 45000 : 60000;
  const result = { arm: ARM, playwrightCore: require(path.join(PW, 'package.json')).version, firefox: EXE, node: process.version, persistent, forced, keyOff: !!env.MOZ_DISABLE_SAFE_MODE_KEY, safeModeRestart: env.MOZ_SAFE_MODE_RESTART ?? null, ...result0, startUtc: new Date().toISOString() };
  w(`START ${JSON.stringify(result)}`);
  let treeAt20 = null;
  const peek = setTimeout(() => { const t = tree(firefoxPid); treeAt20 = summary(t); fs.writeFileSync(path.join(OUT, `${ARM}.tree-at-20s.txt`), t.join('\n')); w(`TREE@20s ${JSON.stringify(treeAt20)}`); }, 20000);
  try {
    if (persistent) {
      const profile = path.join(OUT, `${ARM}-profile`);
      const tl = ms();
      let context;
      try {
        context = await firefox.launchPersistentContext(profile, { executablePath: EXE, headless: true, env, timeout: launchTimeout, handleSIGINT: false });
        result.launch = { ok: true, ms: ms() - tl };
      } catch (e) {
        result.launch = { ok: false, ms: ms() - tl, error: String(e.message || e).split('\n')[0] };
      }
      w(`LAUNCH ${JSON.stringify(result.launch)}`);
      if (context) {
        const page = context.pages()[0] || await context.newPage();
        const tn = ms();
        const nav = await race(page.goto(url, { timeout: 30000 }), 35000, 'goto');
        result.goto = { ok: nav.ok, ms: ms() - tn };
        w(`GOTO ${JSON.stringify(result.goto)}`);
        clearTimeout(peek);
        const t = tree(firefoxPid); result.treeHealthy = summary(t); fs.writeFileSync(path.join(OUT, `${ARM}.tree.txt`), t.join('\n'));
        w(`TREE ${JSON.stringify(result.treeHealthy)}`);
        const c = await race(context.close(), 30000, 'close');
        result.close = c.ok ? 'ok' : 'timed out';
      }
    } else {
      const tl = ms();
      let browser;
      try {
        browser = await firefox.launch({ executablePath: EXE, headless: true, env, timeout: launchTimeout, handleSIGINT: false });
        result.launch = { ok: true, ms: ms() - tl };
      } catch (e) {
        result.launch = { ok: false, ms: ms() - tl, error: String(e.message || e).split('\n')[0] };
      }
      w(`LAUNCH ${JSON.stringify(result.launch)}`);
      if (browser) {
        const tp = ms();
        const np = await race(browser.newPage(), 30000, 'newPage');
        result.newPage = { ok: np.ok, ms: ms() - tp };
        w(`NEWPAGE ${JSON.stringify(result.newPage)}`);
        if (np.ok) {
          const tn = ms();
          const nav = await race(np.v.goto(url, { timeout: 30000 }), 35000, 'goto');
          result.goto = { ok: nav.ok, ms: ms() - tn };
          w(`GOTO ${JSON.stringify(result.goto)}`);
        }
        if (treeAt20 === null) { clearTimeout(peek); const t = tree(firefoxPid); result.treeHealthy = summary(t); fs.writeFileSync(path.join(OUT, `${ARM}.tree.txt`), t.join('\n')); w(`TREE ${JSON.stringify(result.treeHealthy)}`); }
        const c = await race(browser.close(), 30000, 'close');
        result.close = c.ok ? 'ok' : 'timed out';
      }
    }
  } catch (e) {
    result.unexpected = String(e.stack || e);
  }
  clearTimeout(peek);
  result.treeAt20s = treeAt20;
  result.firefoxPid = firefoxPid;
  result.totalMs = ms();
  w(`DONE ${JSON.stringify(result)}`);
  fs.appendFileSync(path.join(OUT, 'results.jsonl'), JSON.stringify(result) + '\n');
  server.close();
  setTimeout(() => process.exit(0), 500);
})();
