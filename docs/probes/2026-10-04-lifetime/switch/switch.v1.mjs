// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The headed and headless switch, end to end through the published
// BrowserAI.Server.exe, and the lifetime corners around it. Run on a desktop
// nobody is looking at (HiddenDesktop.ps1), so a headed browser's window never
// reaches the screen.
//
//   node switch.mjs server=<exe> browser=chromium|firefox scenario=<name> from=headless|headed run=<tag> out=<dir>
//
// Scenarios:
//   switch      sign in, write every store, open three tabs, type into a form;
//               browser_close; browserai_resume with headed toggled; read it all back.
//   conflict    resume while the browser is up: a differing headed, the same one, none.
//   lasttab     close the only tab with browser_tabs, then try the switch.
//   windowclose a headed session whose window is closed the way a person closes it.

import { mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import path from 'node:path';
import { startSite } from './site.mjs';
import { Client, evaluated, tabsOf, descendants, browserProgram } from './mcp.mjs';

const opt = Object.fromEntries(process.argv.slice(2).map((a) => { const i = a.indexOf('='); return [a.slice(0, i), a.slice(i + 1)]; }));
const out = opt.out;
rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });
const run = `${opt.run}-${randomBytes(3).toString('hex')}`;
const sessionDir = path.join(opt.sessions, `${opt.scenario}-${opt.browser}-${opt.from}-${opt.run}`);
rmSync(sessionDir, { recursive: true, force: true });
const fromHeaded = opt.from === 'headed';

const result = { run, scenario: opt.scenario, browser: opt.browser, from: opt.from, session: sessionDir, startedUtc: new Date().toISOString(), steps: [], census: {}, before: [], after: [] };
const save = () => writeFileSync(path.join(out, 'result.json'), JSON.stringify(result, null, 2));

const site = await startSite(run);
const client = new Client(opt.server, { cwd: out, stderrPath: path.join(out, 'server.stderr.log'), logPath: path.join(out, 'calls.log') });
result.serverPid = client.pid;

const READER = `async () => {
  const out = { url: location.href, title: document.title, historyLength: history.length };
  try { out.ls = localStorage.getItem('ls'); } catch (e) { out.ls = 'THREW ' + e.message; }
  try { out.ss = sessionStorage.getItem('ss'); } catch (e) { out.ss = 'THREW ' + e.message; }
  try { out.docCookie = document.cookie; } catch (e) { out.docCookie = 'THREW ' + e.message; }
  try {
    out.idb = await new Promise((resolve) => {
      const open = indexedDB.open('lifetime', 1);
      open.onupgradeneeded = () => open.result.createObjectStore('kv');
      open.onsuccess = () => { try { const get = open.result.transaction('kv', 'readonly').objectStore('kv').get('idb'); get.onsuccess = () => resolve(get.result ?? null); get.onerror = () => resolve('GETERR'); } catch (e) { resolve('THREW ' + e.message); } };
      open.onerror = () => resolve('OPENERR');
    });
  } catch (e) { out.idb = 'THREW ' + e.message; }
  try { out.who = await (await fetch('/whoami', { cache: 'no-store' })).json(); } catch (e) { out.who = 'THREW ' + e.message; }
  const notes = document.querySelector('#notes'); out.notes = notes ? notes.value : null;
  out.visibility = document.visibilityState; out.hasFocus = document.hasFocus(); out.webdriver = navigator.webdriver;
  out.outer = [outerWidth, outerHeight]; out.inner = [innerWidth, innerHeight]; out.screen = [screen.width, screen.height];
  out.ua = navigator.userAgent;
  try { out.brands = navigator.userAgentData ? navigator.userAgentData.brands.map((b) => b.brand + '/' + b.version).join(', ') : null; } catch (e) { out.brands = 'THREW ' + e.message; }
  return JSON.stringify(out);
}`;

const step = async (label, tool, args, timeoutMs) => {
  const withSession = tool.startsWith('browserai_') ? args : { session: sessionDir, why: `lifetime rig ${opt.scenario}: ${label}`, ...args };
  let answer;
  try { answer = await client.call(tool, withSession, timeoutMs); } catch (e) { answer = { ok: false, isError: true, text: `THREW ${e.message}`, ms: null }; }
  result.steps.push({ label, tool, args: Object.fromEntries(Object.entries(args).filter(([k]) => k !== 'function')), ok: answer.ok, isError: answer.isError, ms: answer.ms, text: answer.text.slice(0, 2500) });
  save();
  return answer;
};

const read = async (label) => evaluated((await step(label, 'browser_evaluate', { function: READER })).text);

const census = (label) => {
  try {
    const procs = descendants(client.pid);
    result.census[label] = { program: browserProgram(procs), processes: procs.map((p) => ({ pid: p.pid, parent: p.parent, depth: p.depth, exe: p.exe, cmd: (p.cmd ?? '').slice(0, 1500) })) };
  } catch (e) { result.census[label] = { error: e.message }; }
  save();
};

// The first browser call after a resume starts the browser, and its own session
// restore attaches the tabs as they load, so one listing can be short. The first
// listing is kept as what a caller would see, then the list is asked again once
// a second until it has at least `expected` tabs and has not changed for three
// readings, for at most 20 s.
const firstCallAndSettle = async (label, expected) => {
  const first = tabsOf((await step(`${label}: first browser call (list tabs)`, 'browser_tabs', { action: 'list' })).text);
  const readings = [first.length];
  let last = first;
  let unchanged = 0;
  const started = Date.now();
  while (Date.now() - started < 20000 && !(last.length >= expected && unchanged >= 3)) {
    await new Promise((r) => setTimeout(r, 1000));
    const next = tabsOf((await step(`${label}: list tabs while the restore settles`, 'browser_tabs', { action: 'list' })).text);
    unchanged = next.length === last.length && next.every((t, i) => t.url === last[i].url) ? unchanged + 1 : 0;
    last = next;
    readings.push(next.length);
  }
  return { first, settled: last, readings, settleMs: Date.now() - started };
};

const readAllTabs = async (into, label) => {
  const tabs = tabsOf((await step(`${label}: list tabs`, 'browser_tabs', { action: 'list' })).text);
  for (const tab of tabs) {
    await step(`${label}: select tab ${tab.index}`, 'browser_tabs', { action: 'select', index: tab.index });
    into.push({ index: tab.index, listed: tab, state: await read(`${label}: read tab ${tab.index}`) });
  }
  return tabs;
};

const init = (headed) => step('init', 'browserai_init', {
  directory: sessionDir,
  purpose: `Lifetime review rig, ${opt.scenario} on ${opt.browser}, run ${opt.run}: a local test site only, destroyed at the end of the run.`,
  browser: opt.browser,
  headed,
});

const resume = (label, extra) => step(label, 'browserai_resume', { directory: sessionDir, why: `lifetime rig ${opt.scenario}: ${label}`, ...extra });

const signInAndFill = async () => {
  await step('navigate login', 'browser_navigate', { url: `${site.origin}/login` });
  await step('type user', 'browser_type', { target: '#user', text: 'tester' });
  await step('type password and submit', 'browser_type', { target: '#pass', text: 'not-a-secret', submit: true });
  result.signedIn = await read('read account after sign-in');
  await step('navigate h1', 'browser_navigate', { url: `${site.origin}/h/1` });
  await step('navigate h2', 'browser_navigate', { url: `${site.origin}/h/2` });
  await step('navigate store', 'browser_navigate', { url: `${site.origin}/store` });
  await step('wait for the store page', 'browser_evaluate', { function: "async () => { for (let i = 0; i < 100 && !document.title.startsWith('stored'); i++) await new Promise((r) => setTimeout(r, 100)); return document.title; }" });
  await step('new tab: form', 'browser_tabs', { action: 'new', url: `${site.origin}/form` });
  await step('type into the form', 'browser_type', { target: '#notes', text: `typed ${run}` });
  await step('new tab: account', 'browser_tabs', { action: 'new', url: `${site.origin}/account?tab=2` });
};

try {
  result.handshake = await client.handshake();
  save();

  if (opt.scenario === 'switch') {
    await init(fromHeaded);
    await signInAndFill();
    await readAllTabs(result.before, 'before');
    await step('select the form tab last', 'browser_tabs', { action: 'select', index: 1 });
    census('before');
    await step('browser_close', 'browser_close', {});
    await resume('resume with headed toggled', { headed: !fromHeaded });
    result.restore = await firstCallAndSettle('after', result.before.length);
    result.afterTabs = await readAllTabs(result.after, 'after');
    census('after');
    const storeTab = result.after.find((t) => t.state?.url?.endsWith('/store'));
    if (storeTab) {
      await step('select the store tab', 'browser_tabs', { action: 'select', index: storeTab.index });
      await step('navigate back', 'browser_navigate_back', {});
      result.backTo = await read('read after back');
    }
  } else if (opt.scenario === 'conflict') {
    await init(false);
    await step('navigate h1', 'browser_navigate', { url: `${site.origin}/h/1` });
    census('up');
    await resume('resume headed:true while up', { headed: true });
    await resume('resume headed:false while up', { headed: false });
    await resume('resume bare while up', {});
    await resume('resume viewport while up', { viewport: '1280x720' });
    result.stillThere = await read('read after the resumes');
  } else if (opt.scenario === 'lasttab') {
    await init(false);
    await signInAndFill();
    await step('close tab 2', 'browser_tabs', { action: 'close', index: 2 });
    await step('close tab 1', 'browser_tabs', { action: 'close', index: 1 });
    await step('close the last tab', 'browser_tabs', { action: 'close' });
    census('after closing every tab');
    await step('list tabs after closing every tab', 'browser_tabs', { action: 'list' });
    await resume('resume headed:true after closing every tab', { headed: true });
    await step('browser_close', 'browser_close', {});
    await resume('resume headed:true after browser_close', { headed: true });
    result.restore = await firstCallAndSettle('after', 1);
    result.afterTabs = await readAllTabs(result.after, 'after');
    census('after');
  } else if (opt.scenario === 'windowclose') {
    await init(true);
    await signInAndFill();
    await readAllTabs(result.before, 'before');
    census('before');
    // Closed the way a person closes it: WM_CLOSE to every visible top-level
    // window on this private desktop, posted from a process on the same
    // desktop. The desktop holds nothing but this rig's own processes.
    result.closeWindows = execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', path.join(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), 'close-windows.ps1')], { encoding: 'utf8', windowsHide: true });
    await new Promise((r) => setTimeout(r, 8000));
    census('after the window closed');
    await step('list tabs after the window closed', 'browser_tabs', { action: 'list' });
    await step('list tabs again', 'browser_tabs', { action: 'list' });
    census('after the next calls');
    await step('browser_close with the window gone', 'browser_close', {});
    await resume('resume headless after the window closed', { headed: false });
    result.restore = await firstCallAndSettle('after', result.before.length);
    result.afterTabs = await readAllTabs(result.after, 'after');
    census('after');
  }
} catch (e) {
  result.fatal = String(e?.stack ?? e);
} finally {
  try { await step('destroy', 'browserai_destroy', { directory: sessionDir, why: `lifetime rig ${opt.scenario}: done with run ${opt.run}` }); } catch (e) { result.destroyError = String(e); }
  result.serverExit = await client.close();
  result.siteHits = site.hits;
  await site.close();
  result.endedUtc = new Date().toISOString();
  save();
  setTimeout(() => process.exit(0), 500);
}
