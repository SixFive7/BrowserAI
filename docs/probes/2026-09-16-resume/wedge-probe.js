// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Measures the wedge `resume-probe.js` found on 2026-09-16 and did not diagnose
// (HAZARDS.md, Q207): kill a session's node child under a LIVE BrowserAI, resume
// in the same process, then make a browser call and find out whether it ever
// returns.
//
// The difference from `resume-probe.js` is that NOTHING here awaits an answer
// without a clock on it. The hung call is fired and polled; the wait is bounded
// at the largest product timeout plus margin, stated on the command line, so
// "it never returned" is a measurement rather than the probe giving up at a
// number nobody chose.
//
// Nothing here touches %LocalAppData%\BrowserAI.app. The session directory is
// the one passed in; the data root is the product's own, which is what the suite
// drives too -- so do not run this beside a suite run.
'use strict';
const { spawn, execFileSync } = require('node:child_process');
const path = require('node:path');
const fs = require('node:fs');
const { Mcp, text } = require('./mcp.js');

const exe = process.argv[2];
const sessionDir = process.argv[3];
const outPath = process.argv[4];
const boundMs = Number(process.argv[5] || 15 * 60 * 1000);
const browser = process.argv[6] || 'chromium';

const report = { browser, boundMs, utc: new Date().toISOString(), steps: [] };
const stamp = () => new Date().toISOString();
const say = (k, v) => {
  report[k] = v;
  console.log(stamp(), k, '=', typeof v === 'string' ? v.slice(0, 500) : JSON.stringify(v).slice(0, 500));
};
const note = (what) => {
  report.steps.push({ at: stamp(), what });
  console.log(stamp(), what);
};

function nodeChildrenOf(pid, ownedPrefix) {
  // pid-keyed, never by image name: enumerate by ParentProcessId and keep only
  // children whose ExecutablePath is under a directory BrowserAI owns.
  const ps = `Get-CimInstance Win32_Process -Filter "ParentProcessId=${pid}" | ForEach-Object { '{0}|{1}' -f $_.ProcessId, $_.ExecutablePath }`;
  const out = execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8' });
  return out.split(/\r?\n/).map(l => l.trim()).filter(Boolean)
    .map(l => { const i = l.indexOf('|'); return { pid: Number(l.slice(0, i)), exe: l.slice(i + 1) }; })
    .filter(c => c.exe && c.exe.toLowerCase().startsWith(ownedPrefix.toLowerCase()));
}

function killVerified(pid, exePath) {
  execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command',
    `$p = Get-Process -Id ${pid} -ErrorAction Stop; if ($p.Path -ne '${exePath.replace(/'/g, "''")}') { throw 'identity changed' }; Stop-Process -Id ${pid} -Force`],
    { encoding: 'utf8' });
}

function alive(pid) {
  try {
    const out = execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command',
      `try { $null = Get-Process -Id ${pid} -ErrorAction Stop; 'yes' } catch { 'no' }`], { encoding: 'utf8' });
    return out.trim() === 'yes';
  } catch { return false; }
}

function browsersUnder(root) {
  // By EXECUTABLE PATH under a directory BrowserAI owns, never by image name.
  const ps = `Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.ToLower().StartsWith('${root.toLowerCase().replace(/'/g, "''")}') } | ForEach-Object { $_.ProcessId }`;
  try {
    return execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8' })
      .split(/\r?\n/).map(l => l.trim()).filter(Boolean).map(Number);
  } catch { return []; }
}

const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const pageServer = spawn(process.execPath, [path.join(__dirname, 'page-server.js'), '0'],
    { stdio: ['ignore', 'pipe', 'inherit'], windowsHide: true });
  const port = await new Promise(res => pageServer.stdout.once('data', d => res(JSON.parse(d.toString()).port)));
  const url = `http://127.0.0.1:${port}/`;
  say('origin', url);

  const ownedPayload = path.join(path.dirname(exe), 'payload');
  const browsersRoot = path.join(process.env.LOCALAPPDATA, 'BrowserAI', 'browsers');

  const a = new Mcp(exe);
  await a.initialize();
  say('serverAPid', a.proc.pid);

  const init = await a.tool('browserai_init', {
    directory: sessionDir,
    purpose: 'Measuring the resume wedge recorded in HAZARDS.md on 2026-09-16 and not diagnosed (Q207 b).',
    browser,
  });
  say('initMs', init.ms);

  const nav1 = await a.tool('browser_navigate', { session: sessionDir, why: 'Open a real origin so the session has a live page before its child is killed.', url });
  say('navigate1Ms', nav1.ms);
  say('navigate1', text(nav1).slice(0, 200));

  const children = nodeChildrenOf(a.proc.pid, ownedPayload);
  say('nodeChildren', children);
  if (children.length === 0) throw new Error('no node child under a path BrowserAI owns');

  say('browsersBeforeKill', browsersUnder(browsersRoot).length);
  for (const c of children) killVerified(c.pid, c.exe);
  say('killedAt', stamp());
  say('killed', children.map(c => c.pid));

  await sleep(1500);
  say('nodeChildrenAfterKill', nodeChildrenOf(a.proc.pid, ownedPayload));
  say('browsersAfterKill', browsersUnder(browsersRoot).length);

  const resume = await a.tool('browserai_resume', { directory: sessionDir, why: 'Reproducing the no-op resume the 2026-09-16 run recorded at 7.8 ms.' });
  say('resumeMs', resume.ms);
  say('resume', text(resume).slice(0, 600));

  // ---- THE HUNG CALL, fired and polled rather than awaited -----------------
  note('firing browser_navigate on server A and NOT awaiting it');
  const hungStarted = Date.now();
  let hung = null;
  const hungPromise = a.tool('browser_navigate', { session: sessionDir, why: 'Measuring whether a call against a session whose child is gone ever returns.', url })
    .then(answer => { hung = { at: Date.now(), answer }; return answer; });

  // ---- A SECOND CLIENT, 30 s in ------------------------------------------
  let b = null;
  let secondResume = null;
  let secondNavigate = null;

  const deadline = hungStarted + boundMs;
  let lastLog = 0;

  while (hung === null && Date.now() < deadline) {
    await sleep(5000);
    const elapsed = Math.round((Date.now() - hungStarted) / 1000);

    if (elapsed >= 30 && b === null) {
      note('starting server B and resuming the same directory from it');
      b = new Mcp(exe);
      await b.initialize();
      say('serverBPid', b.proc.pid);
      const r = await b.tool('browserai_resume', { directory: sessionDir, why: 'Asking whether a second client can recover a session whose first server is wedged.' });
      secondResume = { ms: r.ms, text: text(r).slice(0, 800), isError: !!(r.result && r.result.isError) };
      say('secondClientResume', secondResume);

      if (!secondResume.isError) {
        const n = await b.tool('browser_navigate', { session: sessionDir, why: 'Asking whether the recovered session can drive a browser again.', url });
        secondNavigate = { ms: n.ms, text: text(n).slice(0, 400), isError: !!(n.result && n.result.isError) };
        say('secondClientNavigate', secondNavigate);
      }
    }

    if (elapsed - lastLog >= 60 || elapsed < 60) {
      lastLog = elapsed;
      note(`still outstanding at ${elapsed}s - server A alive=${alive(a.proc.pid)} nodeChildren=${nodeChildrenOf(a.proc.pid, ownedPayload).length} browsers=${browsersUnder(browsersRoot).length}`);
    }
  }

  if (hung !== null) {
    say('hungReturnedAfterMs', hung.at - hungStarted);
    say('hungAnswerIsError', !!(hung.answer.result && hung.answer.result.isError));
    say('hungAnswer', text(hung.answer).slice(0, 1200));
  } else {
    say('hungReturnedAfterMs', null);
    say('hungAnswer', `NOTHING CAME BACK inside the bound of ${boundMs} ms.`);
  }

  say('boundReachedAt', stamp());
  say('serverAStillAlive', alive(a.proc.pid));
  say('browsersAtEnd', browsersUnder(browsersRoot).length);

  // ---- teardown: leave nothing behind -------------------------------------
  if (b) { b.close(); }
  a.close();
  await sleep(2000);

  if (alive(a.proc.pid)) {
    note('server A did not exit on stdin close; ending it by pid');
    try { execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', `Stop-Process -Id ${a.proc.pid} -Force`], { encoding: 'utf8' }); } catch { /* already gone */ }
  }

  await sleep(1500);

  const c = new Mcp(exe);
  await c.initialize();
  const destroy = await c.tool('browserai_destroy', { directory: sessionDir, why: 'The probe is finished; leave nothing in the session index.' });
  say('destroy', text(destroy).slice(0, 600));
  c.close();

  await sleep(1000);
  say('browsersAfterDestroy', browsersUnder(browsersRoot).length);

  pageServer.kill();
  report.stderrTailA = a.stderr.slice(-4000);
  if (b) { report.stderrTailB = b.stderr.slice(-2000); }
  fs.writeFileSync(outPath, JSON.stringify(report, null, 2), 'utf8');
  console.log(stamp(), 'wrote', outPath);
  setTimeout(() => process.exit(0), 500);
})().catch(err => {
  report.error = String((err && err.stack) || err);
  try { fs.writeFileSync(outPath, JSON.stringify(report, null, 2), 'utf8'); } catch { /* nothing left to do */ }
  console.error(report.error);
  process.exit(1);
});
