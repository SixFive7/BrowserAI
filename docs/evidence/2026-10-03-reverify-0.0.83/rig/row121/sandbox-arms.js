// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Re-establishes the Chromium half of re-verification row 121 on 2026-10-03:
// the provisioned chrome.exe launched directly, headless, with
// --enable-logging=stderr --v=1, once plain and once with
// --disable-field-trial-config, and each stderr grepped for sandbox_win.cc.
// Present-then-absent is the finding; the plain arm is the positive control.
// The network service is found by descent from the pid launched here, never by
// image name, and its --service-sandbox-type is read off its command line.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { spawn, execFileSync } = require('node:child_process');

const EXE = path.join(process.env.LOCALAPPDATA, 'BrowserAI', 'browsers', 'chromium-1247', 'chrome-win64', 'chrome.exe');
const OUT = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\stale-scratch\\out\\row121';
const ROUNDS = Number(process.argv[2] || 2);
fs.rmSync(OUT, { recursive: true, force: true });
fs.mkdirSync(OUT, { recursive: true });
const log = (...a) => { const s = [new Date().toISOString(), ...a].join(' '); console.log(s); fs.appendFileSync(path.join(OUT, 'sandbox-arms.log'), s + '\n'); };
const sleep = ms => new Promise(r => setTimeout(r, ms));

function descendants(rootPid) {
  const ps = 'Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,ExecutablePath,CommandLine | ConvertTo-Json -Depth 3 -Compress';
  const all = JSON.parse(execFileSync('pwsh', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8', maxBuffer: 256 * 1024 * 1024 }));
  const byParent = new Map();
  for (const p of all) { if (!byParent.has(p.ParentProcessId)) byParent.set(p.ParentProcessId, []); byParent.get(p.ParentProcessId).push(p); }
  const out = []; const stack = [rootPid];
  while (stack.length) { const pid = stack.pop(); for (const ch of (byParent.get(pid) || [])) { out.push(ch); stack.push(ch.ProcessId); } }
  return out;
}

(async () => {
  log('exe', EXE);
  const summary = [];
  for (let round = 1; round <= ROUNDS; round++) {
    for (const arm of ['plain', 'disable-field-trial-config']) {
      const tag = `${arm}-${round}`;
      const profile = path.join(OUT, tag, 'profile');
      fs.mkdirSync(profile, { recursive: true });
      const args = ['--headless', '--enable-logging=stderr', '--v=1', `--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check'];
      if (arm !== 'plain') args.push('--disable-field-trial-config');
      args.push('about:blank');
      const errPath = path.join(OUT, tag, 'stderr.log');
      const errFd = fs.openSync(errPath, 'w');
      const child = spawn(EXE, args, { stdio: ['ignore', 'ignore', errFd], windowsHide: true });
      log(tag, 'pid', child.pid, 'args', JSON.stringify(args));
      await sleep(10000);
      const tree = descendants(child.pid);
      const net = tree.find(p => (p.CommandLine || '').includes('--utility-sub-type=network.mojom.NetworkService'));
      const sbx = net ? ((net.CommandLine.match(/--service-sandbox-type=(\S+)/) || [])[1] || '(none on the command line)') : '(no network service found)';
      try { execFileSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' }); } catch { /* already gone */ }
      await sleep(1500);
      fs.closeSync(errFd);
      const text = fs.readFileSync(errPath, 'utf8');
      const row = {
        tag,
        processes: tree.length + 1,
        networkServicePid: net ? net.ProcessId : null,
        serviceSandboxType: sbx,
        sandboxWinLines: text.split('\n').filter(l => l.includes('sandbox_win.cc')).length,
        networkServiceRestartLines: text.split('\n').filter(l => l.includes('network_service_instance_impl.cc') && /crashed|restarting/i.test(l)).length,
        fieldTrialTestingConfigLines: text.split('\n').filter(l => l.includes('Applying FieldTrialTestingConfig')).length,
        errorLines: text.split('\n').filter(l => /:ERROR:/.test(l)).length,
        stderrBytes: Buffer.byteLength(text),
        firstSandboxLine: (text.split('\n').find(l => l.includes('sandbox_win.cc')) || '').trim().slice(0, 400),
      };
      summary.push(row);
      log(tag, JSON.stringify(row));
    }
  }
  fs.writeFileSync(path.join(OUT, 'summary.json'), JSON.stringify(summary, null, 2));
  log('done');
})().catch(e => { log('FAILED', (e && e.stack) || String(e)); process.exit(1); });
