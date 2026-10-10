// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The behaviour measurements, end to end through a published
// BrowserAI.Server.exe. A headed run goes through HiddenDesktop.ps1, so its
// window is on a desktop nobody is looking at.
//
//   node behave.mjs server=<exe> browser=chromium|firefox scenario=<name> headed=true|false run=<tag> out=<dir> sessions=<dir> [heights=a,b,c]
//
// Scenarios:
//   ua           what a page and the server see of the user agent, the client
//                hints and a worker's user agent
//   tall         full-page and element screenshots of banded pages at the
//                heights given, as PNG and JPEG, to a file and inline
//   windowclose  a headed session whose window is closed the way a person
//                closes it, the exit code the browser leaves, and what the next
//                call, browserai_resume and browserai_catch_up then say
//   kill         the browser's main process ended by the rig with exit code 1
//   lasttab      the only tab closed with browser_tabs
//   ownclose     the session's own browser_close, and the exit code it leaves

import { mkdirSync, writeFileSync, rmSync, readFileSync, existsSync, copyFileSync } from 'node:fs';
import { spawn, execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { startSite } from './site.mjs';
import { Client, evaluated, tabsOf, descendants, browserProgram } from './mcp.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const opt = Object.fromEntries(process.argv.slice(2).map((a) => { const i = a.indexOf('='); return [a.slice(0, i), a.slice(i + 1)]; }));
const out = opt.out;
rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });
const run = `${opt.run}-${randomBytes(3).toString('hex')}`;
const headed = opt.headed === 'true';
const sessionDir = path.join(opt.sessions, `${opt.scenario}-${opt.browser}-${headed ? 'headed' : 'headless'}-${opt.run}`);
rmSync(sessionDir, { recursive: true, force: true });

const result = { run, scenario: opt.scenario, browser: opt.browser, headed, session: sessionDir, startedUtc: new Date().toISOString(), steps: [], census: {} };
const save = () => writeFileSync(path.join(out, 'result.json'), JSON.stringify(result, null, 2));

const site = await startSite(run);
const client = new Client(opt.server, { cwd: out, stderrPath: path.join(out, 'server.stderr.log'), logPath: path.join(out, 'calls.log') });
result.serverPid = client.pid;

const step = async (label, tool, args, timeoutMs) => {
  const withSession = tool.startsWith('browserai_') ? args : { session: sessionDir, why: `behave rig ${opt.scenario}: ${label}`, ...args };
  let answer;
  try { answer = await client.call(tool, withSession, timeoutMs); } catch (e) { answer = { ok: false, isError: true, text: `THREW ${e.message}`, ms: null, images: 0 }; }
  result.steps.push({ label, tool, args: Object.fromEntries(Object.entries(args).filter(([k]) => k !== 'function')), ok: answer.ok, isError: answer.isError, ms: answer.ms, images: answer.images, text: answer.text.slice(0, 6000) });
  save();
  return answer;
};

const census = (label) => {
  try {
    const procs = descendants(client.pid);
    result.census[label] = { program: browserProgram(procs), processes: procs.map((p) => ({ pid: p.pid, parent: p.parent, depth: p.depth, exe: p.exe, created: p.created, cmd: (p.cmd ?? '').slice(0, 1500) })) };
  } catch (e) { result.census[label] = { error: e.message }; }
  save();
  return result.census[label];
};

// Holds the browser's main process by pid and creation time, read off the
// census, and resolves with what exit-watch.ps1 wrote once it ends.
const watchExit = (label, program, terminate = -1, timeoutSec = 120) => {
  const file = path.join(out, `exit-${label}.txt`);
  if (!program?.mainPid || !program?.mainCreated) {
    result[`exit-${label}`] = 'no main browser process in the census';
    save();
    return { armed: Promise.resolve(false), ended: Promise.resolve(null) };
  }
  const child = spawn('pwsh', ['-NoProfile', '-NonInteractive', '-File', path.join(here, 'exit-watch.ps1'),
    '-ProcessId', String(program.mainPid), '-CreatedFileTime', String(program.mainCreated), '-Out', file,
    '-Terminate', String(terminate), '-TimeoutSec', String(timeoutSec)], { windowsHide: true, stdio: 'ignore' });
  const ended = new Promise((resolve) => child.on('exit', () => {
    const text = existsSync(file) ? readFileSync(file, 'utf8').trim() : 'the watcher wrote nothing';
    result[`exit-${label}`] = text;
    save();
    resolve(text);
  }));
  // Nothing is closed until the watcher holds the process: a handle opened after
  // the process ended cannot read its exit code.
  const armed = (async () => {
    const started = Date.now();
    while (!existsSync(`${file}.armed`) && Date.now() - started < 60000) {
      if (existsSync(file)) return false;
      await new Promise((r) => setTimeout(r, 100));
    }
    return existsSync(`${file}.armed`);
  })();
  return { armed, ended };
};

const init = () => step('init', 'browserai_init', {
  directory: sessionDir,
  purpose: `Behave lane rig, ${opt.scenario} on ${opt.browser}, run ${opt.run}: a local test site only, destroyed at the end of the run.`,
  browser: opt.browser,
  headed,
});

const signIn = async () => {
  await step('navigate login', 'browser_navigate', { url: `${site.origin}/login` });
  await step('type user', 'browser_type', { target: '#user', text: 'tester' });
  await step('type password and submit', 'browser_type', { target: '#pass', text: 'not-a-secret', submit: true });
  await step('new tab: form', 'browser_tabs', { action: 'new', url: `${site.origin}/form` });
  await step('type into the form', 'browser_type', { target: '#notes', text: `typed ${run}` });
  // Every kind of state a page keeps, written in the form's tab, so the reads
  // after the resume can say which came back.
  await step('write the stores', 'browser_evaluate', { function: `() => { localStorage.setItem('ls', '${run}'); sessionStorage.setItem('ss', '${run}'); document.cookie = 'jss=${run}; path=/'; document.cookie = 'jsp=${run}; max-age=86400; path=/'; return document.cookie; }` });
};

const READ_STATE = `async () => {
  const out = { url: location.href, cookie: document.cookie };
  try { out.ls = localStorage.getItem('ls'); } catch (e) { out.ls = 'THREW ' + e.message; }
  try { out.ss = sessionStorage.getItem('ss'); } catch (e) { out.ss = 'THREW ' + e.message; }
  const notes = document.querySelector('#notes'); out.notes = notes ? notes.value : null;
  try { out.who = await (await fetch('/whoami', { cache: 'no-store' })).json(); } catch (e) { out.who = 'THREW ' + e.message; }
  return JSON.stringify(out);
}`;

// Selects every tab in turn and reads what it holds.
const readEveryTab = async (label) => {
  const listed = tabsOf((await step(`${label}: list tabs`, 'browser_tabs', { action: 'list' })).text);
  const states = [];
  for (const t of listed) {
    await step(`${label}: select tab ${t.index}`, 'browser_tabs', { action: 'select', index: t.index });
    states.push({ index: t.index, url: t.url, state: evaluated((await step(`${label}: read tab ${t.index}`, 'browser_evaluate', { function: READ_STATE })).text) });
  }
  return states;
};

const READ_UA = `async () => {
  const out = { ua: navigator.userAgent, appVersion: navigator.appVersion, webdriver: navigator.webdriver };
  try {
    const d = navigator.userAgentData;
    out.brands = d ? d.brands.map((b) => b.brand + '/' + b.version).join(', ') : null;
    out.mobile = d ? d.mobile : null;
    out.platform = d ? d.platform : null;
    if (d) {
      const high = await d.getHighEntropyValues(['fullVersionList', 'platformVersion', 'architecture', 'bitness', 'model', 'uaFullVersion']);
      out.fullVersionList = (high.fullVersionList || []).map((b) => b.brand + '/' + b.version).join(', ');
      out.platformVersion = high.platformVersion; out.architecture = high.architecture; out.bitness = high.bitness; out.uaFullVersion = high.uaFullVersion;
    }
  } catch (e) { out.uaDataError = String(e); }
  try { out.fetched = await (await fetch('/headers?from=page', { cache: 'no-store' })).json(); } catch (e) { out.fetched = 'THREW ' + e.message; }
  try {
    out.worker = await new Promise((resolve) => {
      const w = new Worker('/worker.js');
      const t = setTimeout(() => resolve('no answer in 10 s'), 10000);
      w.onmessage = (m) => { clearTimeout(t); resolve(m.data); w.terminate(); };
      w.onerror = (e) => { clearTimeout(t); resolve('worker error ' + e.message); };
    });
  } catch (e) { out.worker = 'THREW ' + e.message; }
  return JSON.stringify(out);
}`;

try {
  result.handshake = await client.handshake();
  save();
  await init();

  if (opt.scenario === 'restore') {
    // Two tabs, closed, then resumed hidden: what the server sees from the first
    // requests of the tabs the session restore reopens.
    await step('navigate echo tab 0', 'browser_navigate', { url: `${site.origin}/echo?tab=0` });
    await step('new tab: echo tab 1', 'browser_tabs', { action: 'new', url: `${site.origin}/echo?tab=1` });
    await step('browser_close', 'browser_close', {});
    const from = site.hits.length;
    await step('resume hidden', 'browserai_resume', { directory: sessionDir, why: `behave rig ${opt.scenario}: resuming hidden to read what the restored tabs send`, headed: false });
    await step('the first call after the resume', 'browser_tabs', { action: 'list' });
    await new Promise((r) => setTimeout(r, 4000));
    result.restoreHits = site.hits.slice(from).map((h) => ({ path: h.path, ua: h.ua, chUa: h.chUa, dest: h.dest }));
    result.page = evaluated((await step('read the user agent from the page', 'browser_evaluate', { function: READ_UA })).text);
  } else if (opt.scenario === 'ua') {
    await step('navigate echo', 'browser_navigate', { url: `${site.origin}/echo` });
    await step('navigate echo again, so Accept-CH has been seen', 'browser_navigate', { url: `${site.origin}/echo?second=1` });
    result.page = evaluated((await step('read the user agent from the page', 'browser_evaluate', { function: READ_UA })).text);
    census('up');
  } else if (opt.scenario === 'tall') {
    const heights = (opt.heights ?? '16384,16385,20000').split(',').map(Number);
    result.images = [];
    for (const h of heights) {
      await step(`navigate tall ${h}`, 'browser_navigate', { url: `${site.origin}/tall?h=${h}` });
      const shot = async (label, args) => {
        const answer = await step(label, 'browser_take_screenshot', args);
        const file = answer.text.match(/\]\(([^)]+)\)/)?.[1] ?? null;
        result.images.push({ label, height: h, args, isError: answer.isError, images: answer.images, file, text: answer.text.slice(0, 1500) });
        if (file && existsSync(file)) { try { copyFileSync(file, path.join(out, path.basename(file))); } catch { /* left where it is */ } }
        save();
      };
      await shot(`png file ${h}`, { fullPage: true, type: 'png', filename: `tall-${h}.png` });
      await shot(`jpeg file ${h}`, { fullPage: true, type: 'jpeg', filename: `tall-${h}.jpeg` });
      await shot(`png inline ${h}`, { fullPage: true, type: 'png' });
    }
    if (opt.element) {
      const h = Number(opt.element);
      await step(`navigate tall element ${h}`, 'browser_navigate', { url: `${site.origin}/tallel?h=${h}` });
      const snap = await step('snapshot for the element ref', 'browser_snapshot', {});
      const ref = snap.text.match(/img "the tall element" \[ref=([^\]]+)\]/)?.[1] ?? snap.text.match(/\[ref=(e\d+)\][^\n]*the tall element/)?.[1] ?? null;
      result.elementRef = ref;
      if (ref) {
        const answer = await step(`element png file ${h}`, 'browser_take_screenshot', { target: ref, type: 'png', filename: `element-${h}.png` });
        const file = answer.text.match(/\]\(([^)]+)\)/)?.[1] ?? null;
        result.images.push({ label: `element png file ${h}`, height: h, element: true, isError: answer.isError, images: answer.images, file, text: answer.text.slice(0, 1500) });
        if (file && existsSync(file)) { try { copyFileSync(file, path.join(out, path.basename(file))); } catch { /* left */ } }
      }
    }
    for (const w of (opt.wides ?? '').split(',').filter(Boolean).map(Number)) {
      await step(`navigate wide ${w}`, 'browser_navigate', { url: `${site.origin}/wide?w=${w}` });
      const answer = await step(`wide png file ${w}`, 'browser_take_screenshot', { fullPage: true, type: 'png', filename: `wide-${w}.png` });
      const file = answer.text.match(/\]\(([^)]+)\)/)?.[1] ?? null;
      result.images.push({ label: `wide png file ${w}`, width: w, isError: answer.isError, images: answer.images, file, text: answer.text.slice(0, 1500) });
      if (file && existsSync(file)) { try { copyFileSync(file, path.join(out, path.basename(file))); } catch { /* left */ } }
    }
    save();
  } else if (opt.scenario === 'windowclose' || opt.scenario === 'kill') {
    await signIn();
    result.stateBefore = await readEveryTab('before');
    const before = census('before');
    const killing = opt.scenario === 'kill';
    const exit = watchExit(opt.scenario, before.program, killing ? 1 : -1);
    result.watchArmed = await exit.armed;
    if (!killing) {
      // Closed the way a person closes it: WM_CLOSE to every visible top-level
      // window on this private desktop, posted from a process on that desktop.
      try {
        result.closeWindows = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', path.join(here, 'close-windows.ps1')], { encoding: 'utf8', windowsHide: true });
      } catch (e) { result.closeWindows = `THREW ${e.message}`; }
    }
    await exit.ended;
    await new Promise((r) => setTimeout(r, 3000));
    census('after the browser ended');
    await step('the next call after the browser ended', 'browser_tabs', { action: 'list' });
    census('after the next call');
    await step('resume after the browser ended', 'browserai_resume', { directory: sessionDir, why: `behave rig ${opt.scenario}: resuming after the browser ended` });
    await step('catch_up after the resume', 'browserai_catch_up', { session: sessionDir, why: `behave rig ${opt.scenario}: reading what the record says about the close` });
    const tabs = await step('the first call after the resume', 'browser_tabs', { action: 'list' });
    result.tabsAfter = tabsOf(tabs.text);
    await new Promise((r) => setTimeout(r, 3000));
    result.stateAfter = await readEveryTab('after');
  } else if (opt.scenario === 'lasttab') {
    await step('navigate h1', 'browser_navigate', { url: `${site.origin}/h/1` });
    const before = census('before');
    const exit = watchExit('lasttab', before.program, -1, 20);
    result.watchArmed = await exit.armed;
    await step('close the only tab', 'browser_tabs', { action: 'close' });
    await exit.ended;
    census('after closing the only tab');
    await step('the next call', 'browser_tabs', { action: 'list' });
    census('after the next call');
  } else if (opt.scenario === 'ownclose') {
    await step('navigate h1', 'browser_navigate', { url: `${site.origin}/h/1` });
    const before = census('before');
    const exit = watchExit('ownclose', before.program, -1, 90);
    result.watchArmed = await exit.armed;
    await step('browser_close', 'browser_close', {});
    await exit.ended;
    census('after the close');
    await step('the next call', 'browser_tabs', { action: 'list' });
  }
} catch (e) {
  result.fatal = String(e?.stack ?? e);
} finally {
  try { await step('destroy', 'browserai_destroy', { directory: sessionDir, why: `behave rig ${opt.scenario}: done with run ${opt.run}` }); } catch (e) { result.destroyError = String(e); }
  result.serverExit = await client.close();
  result.siteHits = site.hits;
  await site.close();
  result.endedUtc = new Date().toISOString();
  save();
  setTimeout(() => process.exit(0), 500);
}
