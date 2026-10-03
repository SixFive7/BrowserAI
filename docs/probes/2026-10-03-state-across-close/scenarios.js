// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// scenarios.js -- node scenarios.js <scenario> <version 082|083> <browser chromium|firefox> <runTag> [variant]
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const L = require('./lib.js');
const { wait, now, makeConfig, Run, text, isError, evalResult, parseTabs, refsIn, WRITE, READ, COOKIES_CODE, GRANT_CODE, fileInfo, listTree, summarise } = L;

const [scenario, version, browser, runTag, variant] = process.argv.slice(2);
const name = `${scenario}-${version}-${browser}${variant ? '-' + variant : ''}-${runTag}`;

async function fillState(run, child, origin, { withPermission = true } = {}) {
  await run.call(child, 'navigate /a (first browser launch; sets pc persistent + sc session cookies by header)', 'browser_navigate', { url: origin + '/a' });
  await run.call(child, 'navigate /b?x=1', 'browser_navigate', { url: origin + '/b?x=1' });
  const snap = await run.call(child, 'snapshot /b', 'browser_snapshot', {});
  const nameRef = refsIn(snap.t).find(r => r.role === 'textbox' && /Name/i.test(r.name)) || refsIn(snap.t).find(r => r.role === 'textbox');
  await run.call(child, 'type into #name', 'browser_type', { target: nameRef ? nameRef.ref : '#name', text: 'typed-name-value' });
  await run.call(child, 'write stores + scroll', 'browser_evaluate', { function: WRITE });
  if (withPermission) await run.call(child, 'grant geolocation at runtime', 'browser_run_code_unsafe', { code: GRANT_CODE });
  const before = await run.call(child, 'READ before close', 'browser_evaluate', { function: READ });
  const cookies = await run.call(child, 'cookies before close', 'browser_run_code_unsafe', { code: COOKIES_CODE });
  await run.call(child, 'tabs new /c#frag', 'browser_tabs', { action: 'new', url: origin + '/c#frag' });
  const tabsBefore = await run.call(child, 'tabs list before close', 'browser_tabs', { action: 'list' });
  const cSnap = await run.call(child, 'snapshot /c (refs the agent holds)', 'browser_snapshot', {});
  const staleRef = (refsIn(cSnap.t).find(r => r.role === 'link') || refsIn(cSnap.t)[0] || {}).ref;
  return { before: evalResult(before.r), cookies: evalResult(cookies.r), tabsBefore: parseTabs(tabsBefore.t), staleRef };
}

// ------------------------------------------------------------------ S
async function scenarioS() {
  const run = new Run(name);
  const { server, origin } = await L.startPageServer();
  run.note('meta', { scenario, version, browser, runTag, origin });
  const config = makeConfig({ browser, session: run.session });
  try {
    // Child 1: fill, close, look.
    const c1 = await run.spawnChild(version, config);
    const tools = await c1.send('tools/list', {});
    run.note('toolNames', (tools.result.tools || []).map(t => t.name));
    const filled = await fillState(run, c1, origin);
    run.note('filled', filled);
    const mOpen = await run.snap('job with browser open on 2 local tabs', c1);
    const close1 = await run.call(c1, 'browser_close #1 (browser open)', 'browser_close', {});
    const mJustAfter = await run.snap('job immediately after browser_close #1', c1);
    await wait(1000);
    const mNodeOnly = await run.snap('job 1 s after browser_close #1 (node alone)', c1);

    // What the agent sees on the first call afterwards, and what survived.
    const first = await run.call(c1, 'FIRST CALL AFTER CLOSE: browser_snapshot (lazy relaunch)', 'browser_snapshot', {});
    const mRelaunched = await run.snap('job after the lazy relaunch', c1);
    const click = await run.call(c1, 'click the ref held from before the close', 'browser_click', { target: filled.staleRef || 'e2', element: 'link held from before the close' });
    const tabsAfter = await run.call(c1, 'tabs list after relaunch', 'browser_tabs', { action: 'list' });
    const back = await run.call(c1, 'navigate_back after relaunch', 'browser_navigate_back', {});
    await run.call(c1, 'navigate /b?x=1 again', 'browser_navigate', { url: origin + '/b?x=1' });
    const after = await run.call(c1, 'READ after relaunch', 'browser_evaluate', { function: READ });
    const cookiesAfter = await run.call(c1, 'cookies after relaunch', 'browser_run_code_unsafe', { code: COOKIES_CODE });
    run.note('survival', {
      before: filled.before, after: evalResult(after.r), cookiesBefore: filled.cookies, cookiesAfter: evalResult(cookiesAfter.r),
      tabsBefore: filled.tabsBefore, tabsAfter: parseTabs(tabsAfter.t),
      firstCallText: first.t.slice(0, 1500), clickText: click.t.slice(0, 800), clickIsError: isError(click.r), backText: back.t.slice(0, 600),
    });
    // Design B: the whole child goes while the browser is open.
    const tdB = await run.teardown(c1, 'DESIGN B teardown: stdin close with the browser open');

    // Child 2: a fresh child meets the directory.
    const c2 = await run.spawnChild(version, config);
    const firstNav2 = await run.call(c2, 'child-2 first call: navigate /b?x=1 (browser launch)', 'browser_navigate', { url: origin + '/b?x=1' });
    const after2 = await run.call(c2, 'READ after design-B cycle', 'browser_evaluate', { function: READ });
    const tabs2 = await run.call(c2, 'tabs after design-B cycle', 'browser_tabs', { action: 'list' });
    // Q328 b: a browser_close when no browser is open.
    const close2 = await run.call(c2, 'browser_close #2 (browser open)', 'browser_close', {});
    await wait(1000);
    const mNode2 = await run.snap('child-2 node alone', c2);
    const w0 = await run.job.cmd({ op: 'watchstart', interval: 2 });
    const close3 = await run.call(c2, 'browser_close #3 (NO browser open)', 'browser_close', {});
    const w1 = await run.job.cmd({ op: 'watchstop' });
    await wait(1000);
    const mAfter3 = await run.snap('child-2 after close #3 + 1 s', c2);
    // and a second sample of the same thing
    const w2 = await run.job.cmd({ op: 'watchstart', interval: 2 });
    const close4 = await run.call(c2, 'browser_close #4 (NO browser open, again)', 'browser_close', {});
    const w3 = await run.job.cmd({ op: 'watchstop' });
    const tdNode = await run.teardown(c2, 'teardown of a node-alone child');
    run.note('q328b', {
      close3: { ms: close3.ms, text: close3.t, isError: isError(close3.r), watch: w1, watchStart: w0 },
      close4: { ms: close4.ms, text: close4.t, isError: isError(close4.r), watch: w3, watchStart: w2 },
    });
    run.note('timings', {
      close1Ms: close1.ms, relaunchFirstCallMs: first.ms, teardownBrowserOpen: tdB, child2SpawnToInitMs: null,
      child2FirstNavMs: firstNav2.ms, close2Ms: close2.ms, teardownNodeOnly: tdNode,
    });
    run.note('memory', { open2LocalTabs: mOpen, justAfterClose: mJustAfter, nodeOnly1s: mNodeOnly, relaunched: mRelaunched, child2NodeOnly: mNode2, afterClose3: mAfter3 });
    run.note('designB', { after: evalResult(after2.r), tabs: parseTabs(tabs2.t) });
  } catch (e) {
    run.note('error', String(e && e.stack || e));
  } finally {
    server.close();
    await run.finish();
  }
}

// ------------------------------------------------------------------ H (HAR, Q328 c)
async function scenarioH() {
  const run = new Run(name);
  const { server, origin } = await L.startPageServer();
  const har = path.join(run.session, 'output', 'network-20261003-000000000.har');
  run.note('meta', { scenario, version, browser, runTag, origin, har });
  const config = makeConfig({ browser, session: run.session, har });
  const harState = label => {
    const info = fileInfo(har);
    if (info.exists) {
      try {
        const j = JSON.parse(fs.readFileSync(har, 'utf8'));
        info.entries = j.log.entries.length;
        info.pages = (j.log.pages || []).length;
        info.paths = [...new Set(j.log.entries.map(e => { try { return new URL(e.request.url).pathname; } catch { return e.request.url; } }))];
      } catch (e) { info.parseError = String(e.message).slice(0, 200); }
    }
    run.step('HAR ' + label, { har: info });
    return info;
  };
  try {
    const c1 = await run.spawnChild(version, config);
    harState('before any browser');
    await run.call(c1, 'navigate /a', 'browser_navigate', { url: origin + '/a' });
    await run.call(c1, 'navigate /b?x=1', 'browser_navigate', { url: origin + '/b?x=1' });
    const h0 = harState('browser 1 open after /a and /b');
    await run.call(c1, 'browser_close #1', 'browser_close', {});
    const h1 = harState('after browser_close #1');
    await run.call(c1, 'navigate /c (relaunch, browser 2)', 'browser_navigate', { url: origin + '/c' });
    const h2 = harState('browser 2 open after /c');
    await run.call(c1, 'browser_close #2', 'browser_close', {});
    const h3 = harState('after browser_close #2');
    const w0 = await run.job.cmd({ op: 'watchstart', interval: 2 });
    await run.call(c1, 'browser_close #3 (no browser open)', 'browser_close', {});
    const w1 = await run.job.cmd({ op: 'watchstop' });
    const h4 = harState('after browser_close #3 with no browser open');
    await run.call(c1, 'navigate /a (relaunch, browser 4)', 'browser_navigate', { url: origin + '/a' });
    const h5 = harState('browser 4 open after /a');
    await run.teardown(c1, 'teardown with browser 4 open');
    const h6 = harState('after graceful teardown with browser 4 open');
    run.note('har', { h0, h1, h2, h3, h4, h5, h6, close3Watch: w1, outputDir: listTree(path.join(run.session, 'output')) });
  } catch (e) {
    run.note('error', String(e && e.stack || e));
  } finally {
    server.close();
    await run.finish();
  }
}

// ------------------------------------------------------------------ R (the browser's own session restore)
function seedChromiumPreferences(session, restoreOnStartup) {
  const dir = path.join(session, 'profile', 'Default');
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, 'Preferences'), JSON.stringify({ session: { restore_on_startup: restoreOnStartup } }));
}
function readChromiumPreferences(session) {
  try {
    const p = JSON.parse(fs.readFileSync(path.join(session, 'profile', 'Default', 'Preferences'), 'utf8'));
    return { restore_on_startup: p.session && p.session.restore_on_startup, exit_type: p.profile && p.profile.exit_type, exited_cleanly: p.profile && p.profile.exited_cleanly };
  } catch (e) { return { error: String(e.message) }; }
}
function profileFiles(session, browser) {
  const prof = path.join(session, 'profile');
  const all = listTree(prof);
  const keep = browser === 'firefox'
    ? all.filter(f => /sessionstore|prefs\.js|user\.js|sessionCheckpoints/i.test(f.path))
    : all.filter(f => /Sessions|Session Storage|Current Session|Last Session|Preferences$/i.test(f.path));
  return keep;
}
function firefoxPrefLines(session) {
  try { return fs.readFileSync(path.join(session, 'profile', 'prefs.js'), 'utf8').split(/\r?\n/).filter(l => /startup\.page|sessionstore|resume/i.test(l)); } catch { return ['(no prefs.js)']; }
}

async function scenarioR() {
  const run = new Run(name);
  const { server, origin, requests } = await L.startPageServer();
  run.note('meta', { scenario, version, browser, runTag, origin, variant });
  let extraArgs = [], firefoxPrefs = null, ignoreDefaultArgs = null;
  // 'nb' suffix: Playwright's own startup URL is dropped through launchOptions.ignoreDefaultArgs.
  const noBlank = /nb$/.test(variant || '');
  if (noBlank) ignoreDefaultArgs = ['about:blank'];
  const oneShot = /^oneshot/.test(variant || '');
  if (browser === 'chromium') {
    if (variant === 'pref' || variant === 'both') seedChromiumPreferences(run.session, 1);
    if (variant === 'flag' || variant === 'both' || variant === 'flagnb') extraArgs = ['--restore-last-session'];
  } else {
    if (oneShot) firefoxPrefs = { 'browser.sessionstore.restore_on_demand': false, 'browser.sessionstore.restore_tabs_lazily': false };
    if (variant === 'ffeagernb') firefoxPrefs = { 'browser.sessionstore.resume_session_once': true, 'browser.sessionstore.restore_on_demand': false, 'browser.sessionstore.restore_tabs_lazily': false };
    if (variant === 'ffpref') firefoxPrefs = { 'browser.startup.page': 3 };
    if (variant === 'ffonce') firefoxPrefs = { 'browser.sessionstore.resume_session_once': true };
    if (variant === 'ffboth') firefoxPrefs = { 'browser.startup.page': 3, 'browser.sessionstore.resume_session_once': true };
    if (variant === 'ffeager') firefoxPrefs = { 'browser.sessionstore.resume_session_once': true, 'browser.sessionstore.restore_on_demand': false, 'browser.sessionstore.restore_tabs_lazily': false };
    if (variant === 'userjs') fs.writeFileSync(path.join(run.session, 'profile', 'user.js'),
      'user_pref("browser.startup.page", 3);\nuser_pref("browser.sessionstore.resume_from_crash", true);\n');
  }
  const config = makeConfig({ browser, session: run.session, extraArgs, firefoxPrefs, ignoreDefaultArgs });
  // One-shot restore: written into the profile while no browser is running, consumed by the next launch only.
  const armOneShot = async (label, child) => {
    if (!oneShot) return;
    const sum = await run.snap(`job before arming one-shot restore (${label})`, child);
    let detail;
    if (browser === 'chromium') {
      const p = path.join(run.session, 'profile', 'Local State');
      const j = JSON.parse(fs.readFileSync(p, 'utf8'));
      const before = j.was && j.was.restarted;
      j.was = Object.assign({}, j.was, { restarted: true });
      fs.writeFileSync(p, JSON.stringify(j));
      detail = { file: 'Local State', key: 'was.restarted', before, after: true };
    } else {
      const p = path.join(run.session, 'profile', 'prefs.js');
      let t = fs.readFileSync(p, 'utf8');
      const had = t.split(/\r?\n/).filter(l => l.includes('"browser.sessionstore.resume_session_once"'));
      t = t.split(/\r?\n/).filter(l => !l.includes('"browser.sessionstore.resume_session_once"')).join('\n') + '\nuser_pref("browser.sessionstore.resume_session_once", true);\n';
      fs.writeFileSync(p, t);
      detail = { file: 'prefs.js', key: 'browser.sessionstore.resume_session_once', before: had, after: true };
    }
    run.step(`ARMED one-shot restore (${label})`, { browserProcsAtArming: sum.browserProcs, ...detail });
  };
  try {
    const c1 = await run.spawnChild(version, config);
    const filled = await fillState(run, c1, origin, { withPermission: false });
    // A third tab whose page is the result of a POST, so a restore has one to get wrong.
    await run.call(c1, 'tabs new /b?x=2', 'browser_tabs', { action: 'new', url: origin + '/b?x=2' });
    await run.call(c1, 'submit the form by POST', 'browser_evaluate', { function: `async () => { document.getElementById('name').value = 'posted-name'; document.getElementById('f').submit(); return 'submitted'; }` });
    await wait(800);
    await run.call(c1, 'select tab 1 (/c#frag) as current', 'browser_tabs', { action: 'select', index: 1 });
    const tabsBefore3 = await run.call(c1, 'tabs list before close (3 tabs)', 'browser_tabs', { action: 'list' });
    filled.tabsBefore = parseTabs(tabsBefore3.t);
    run.note('filled', filled);
    run.step('profile before close', { files: profileFiles(run.session, browser), prefs: browser === 'chromium' ? readChromiumPreferences(run.session) : firefoxPrefLines(run.session) });
    const reqMark = requests.length;
    await run.call(c1, 'browser_close #1', 'browser_close', {});
    await wait(500);
    let sessionStoreAfterClose = null;
    if (browser === 'firefox') {
      const src = path.join(run.session, 'profile', 'sessionstore.jsonlz4');
      if (fs.existsSync(src)) {
        fs.copyFileSync(src, path.join(run.dir, 'sessionstore-after-close1.jsonlz4'));
        try { sessionStoreAfterClose = JSON.parse(require('node:child_process').execFileSync(process.execPath, [path.join(__dirname, 'mozlz4.js'), src], { encoding: 'utf8' })); } catch (e) { sessionStoreAfterClose = { error: String(e.message).slice(0, 300) }; }
      }
    }
    run.step('profile after close', { files: profileFiles(run.session, browser), prefs: browser === 'chromium' ? readChromiumPreferences(run.session) : firefoxPrefLines(run.session), sessionStoreAfterClose });
    await armOneShot('after idle-style close #1', c1);
    const first = await run.call(c1, 'FIRST CALL AFTER CLOSE: browser_tabs list (relaunch)', 'browser_tabs', { action: 'list' });
    await wait(2500); // let restored tabs load
    const tabsLater = await run.call(c1, 'tabs list 2.5 s later', 'browser_tabs', { action: 'list' });
    const tabs = parseTabs(tabsLater.t);
    const perTab = [];
    for (const tab of tabs) {
      await run.call(c1, `select tab ${tab.index}`, 'browser_tabs', { action: 'select', index: tab.index });
      const rd = await run.call(c1, `READ tab ${tab.index}`, 'browser_evaluate', { function: READ });
      perTab.push({ tab, read: evalResult(rd.r) });
    }
    // Can the agent drive a restored tab? Pick the one on /b, snapshot it, type into it.
    const bTab = tabs.find(t => /\/b\?x=1/.test(t.url));
    let drive = null;
    if (bTab) {
      await run.call(c1, 'select restored /b tab', 'browser_tabs', { action: 'select', index: bTab.index });
      const s = await run.call(c1, 'snapshot restored /b tab', 'browser_snapshot', {});
      const ref = (refsIn(s.t).find(r => r.role === 'textbox' && /Notes/i.test(r.name)) || {}).ref;
      const ty = ref ? await run.call(c1, 'type into restored tab via a fresh ref', 'browser_type', { target: ref, text: 'after-restore' }) : null;
      const back = await run.call(c1, 'navigate_back on restored /b tab', 'browser_navigate_back', {});
      const rd = await run.call(c1, 'READ after back', 'browser_evaluate', { function: READ });
      drive = { snapshotOk: !isError(s.r), ref, typeOk: ty ? !isError(ty.r) : null, typeText: ty ? ty.t.slice(0, 400) : null, backText: back.t.slice(0, 600), afterBack: evalResult(rd.r) };
    }
    const requestsByRestore = requests.slice(reqMark);
    run.note('restore', { firstCallText: first.t.slice(0, 1500), firstCallMs: first.ms, tabsAfterFirst: parseTabs(first.t), tabsLater: tabs, perTab, drive, tabsBefore: filled.tabsBefore, before: filled.before, requestsByRestore });
    // Cycle 2: the CALLER's own browser_close -- nothing is armed. Does it restore anyway?
    await run.call(c1, 'browser_close #2 (as if the caller sent it; nothing armed)', 'browser_close', {});
    const second = await run.call(c1, 'CYCLE 2 first call: browser_tabs list', 'browser_tabs', { action: 'list' });
    await wait(1500);
    const second2 = await run.call(c1, 'cycle 2 tabs 1.5 s later', 'browser_tabs', { action: 'list' });
    // Across a child: put one known page up, graceful teardown with the browser open, then a fresh child.
    await run.call(c1, 'navigate current tab to /c?child=1 (known state for the cross-child check)', 'browser_navigate', { url: origin + '/c?child=1' });
    await run.teardown(c1, 'teardown with the browser open');
    await armOneShot('after a whole-child teardown (design B)', null);
    const c2 = await run.spawnChild(version, config);
    const third = await run.call(c2, 'CHILD-2 first call: browser_tabs list', 'browser_tabs', { action: 'list' });
    await wait(1500);
    const third2 = await run.call(c2, 'child-2 tabs 1.5 s later', 'browser_tabs', { action: 'list' });
    run.note('cycles', { cycle2: parseTabs(second2.t), cycle2FirstMs: second.ms, child2: parseTabs(third2.t), child2FirstMs: third.ms });
    await run.teardown(c2, 'teardown child-2');
    run.note('requests', requests);
  } catch (e) {
    run.note('error', String(e && e.stack || e));
  } finally {
    server.close();
    await run.finish();
  }
}

// ------------------------------------------------------------------ T (BrowserAI reopens recorded tab URLs)
async function scenarioT() {
  const run = new Run(name);
  const { server, origin } = await L.startPageServer();
  run.note('meta', { scenario, version, browser, runTag, origin });
  const config = makeConfig({ browser, session: run.session });
  try {
    const c1 = await run.spawnChild(version, config);
    const filled = await fillState(run, c1, origin, { withPermission: false });
    // A third tab whose page is the result of a POST.
    await run.call(c1, 'tabs new /b?x=2', 'browser_tabs', { action: 'new', url: origin + '/b?x=2' });
    await run.call(c1, 'submit the form by POST', 'browser_evaluate', { function: `async () => { document.getElementById('name').value = 'posted-name'; document.getElementById('f').submit(); return 'submitted'; }` });
    await wait(800);
    await run.call(c1, 'select tab 1 (/c#frag) as current', 'browser_tabs', { action: 'select', index: 1 });
    const listBefore = await run.call(c1, 'tabs list before close (what BrowserAI would record)', 'browser_tabs', { action: 'list' });
    const recorded = parseTabs(listBefore.t);
    const readsBefore = [];
    for (const tab of recorded) {
      await run.call(c1, `select ${tab.index}`, 'browser_tabs', { action: 'select', index: tab.index });
      readsBefore.push(evalResult((await run.call(c1, `READ before ${tab.index}`, 'browser_evaluate', { function: READ })).r));
    }
    await run.call(c1, 'reselect the current tab', 'browser_tabs', { action: 'select', index: recorded.findIndex(t => t.current) });
    await run.call(c1, 'browser_close', 'browser_close', {});
    // The reopen, as BrowserAI would have to do it: one upstream call per tab.
    const t0 = now();
    const calls = [];
    for (let i = 0; i < recorded.length; i++) {
      const tab = recorded[i];
      const c = i === 0
        ? await run.call(c1, `reopen tab 0 by browser_navigate ${tab.url}`, 'browser_navigate', { url: tab.url })
        : await run.call(c1, `reopen tab ${i} by browser_tabs new ${tab.url}`, 'browser_tabs', { action: 'new', url: tab.url });
      calls.push({ ms: c.ms, isError: isError(c.r) });
    }
    const cur = recorded.findIndex(t => t.current);
    const sel = await run.call(c1, `select the recorded current tab ${cur}`, 'browser_tabs', { action: 'select', index: cur });
    const reopenMs = now() - t0;
    const listAfter = await run.call(c1, 'tabs list after reopen', 'browser_tabs', { action: 'list' });
    const reopened = parseTabs(listAfter.t);
    const readsAfter = [];
    for (const tab of reopened) {
      await run.call(c1, `select ${tab.index}`, 'browser_tabs', { action: 'select', index: tab.index });
      readsAfter.push(evalResult((await run.call(c1, `READ after ${tab.index}`, 'browser_evaluate', { function: READ })).r));
    }
    run.note('reopen', { recorded, reopened, readsBefore, readsAfter, reopenMs, calls, selectMs: sel.ms, upstreamCalls: recorded.length + 1 });
    await run.teardown(c1, 'teardown');
  } catch (e) {
    run.note('error', String(e && e.stack || e));
  } finally {
    server.close();
    await run.finish();
  }
}

// ------------------------------------------------------------------ M (memory: ordinary pages, ways to shed it)
const ORDINARY = ['https://en.wikipedia.org/wiki/Web_browser', 'https://developer.mozilla.org/en-US/docs/Web/HTTP', 'https://github.com/microsoft/playwright-mcp'];
async function scenarioM() {
  const run = new Run(name);
  run.note('meta', { scenario, version, browser, runTag, pages: ORDINARY });
  const config = makeConfig({ browser, session: run.session });
  const settle = 6000;
  const m = {};
  try {
    const c1 = await run.spawnChild(version, config);
    m.nodeBeforeAnyBrowser = await run.snap('node before any browser', c1);
    const nav0 = await run.call(c1, 'navigate tab 0', 'browser_navigate', { url: ORDINARY[0] });
    await run.call(c1, 'tabs new 1', 'browser_tabs', { action: 'new', url: ORDINARY[1] });
    await run.call(c1, 'tabs new 2', 'browser_tabs', { action: 'new', url: ORDINARY[2] });
    await wait(settle);
    m.open3 = await run.snap('3 ordinary pages open, settled', c1);
    // c1: every tab to about:blank
    for (let i = 0; i < 3; i++) {
      await run.call(c1, `select ${i}`, 'browser_tabs', { action: 'select', index: i });
      await run.call(c1, `tab ${i} to about:blank`, 'browser_navigate', { url: 'about:blank' });
    }
    await wait(settle);
    m.blank3 = await run.snap('3 tabs on about:blank, settled', c1);
    await run.call(c1, 'close tab 2', 'browser_tabs', { action: 'close', index: 2 });
    await run.call(c1, 'close tab 1', 'browser_tabs', { action: 'close', index: 1 });
    await wait(settle);
    m.blank1 = await run.snap('1 tab on about:blank, settled', c1);
    // reopen the three, then try what upstream offers to shed memory
    await run.call(c1, 'renavigate tab 0', 'browser_navigate', { url: ORDINARY[0] });
    await run.call(c1, 'tabs new 1 again', 'browser_tabs', { action: 'new', url: ORDINARY[1] });
    await run.call(c1, 'tabs new 2 again', 'browser_tabs', { action: 'new', url: ORDINARY[2] });
    await wait(settle);
    m.open3again = await run.snap('3 ordinary pages open again, settled', c1);
    if (browser === 'chromium') {
      const pressure = await run.call(c1, 'CDP memory pressure + GC on every page (run_code_unsafe)', 'browser_run_code_unsafe', { code:
        `async (page) => { const out = []; for (const p of page.context().pages()) { const s = await page.context().newCDPSession(p);
          await s.send('HeapProfiler.collectGarbage').catch(e => out.push('gc:' + e.message));
          await s.send('Memory.simulatePressureNotification', { level: 'critical' }).catch(e => out.push('mp:' + e.message));
          await s.detach(); } return 'done ' + out.join(';'); }` });
      await wait(settle);
      m.pressure = await run.snap('after CDP critical memory pressure + GC', c1);
      const freeze = await run.call(c1, 'CDP freeze every page (run_code_unsafe)', 'browser_run_code_unsafe', { code:
        `async (page) => { const out = []; for (const p of page.context().pages()) { const s = await page.context().newCDPSession(p);
          await s.send('Page.setWebLifecycleState', { state: 'frozen' }).catch(e => out.push('fz:' + e.message)); await s.detach(); } return 'done ' + out.join(';'); }` });
      await wait(settle);
      m.frozen = await run.snap('after CDP freeze of every page', c1);
      run.step('shed results', { pressure: pressure.t.slice(0, 300), freeze: freeze.t.slice(0, 300) });
      const unfreeze = await run.call(c1, 'unfreeze', 'browser_run_code_unsafe', { code:
        `async (page) => { for (const p of page.context().pages()) { const s = await page.context().newCDPSession(p); await s.send('Page.setWebLifecycleState', { state: 'active' }).catch(() => {}); await s.detach(); } return 'ok'; }` });
    }
    const close = await run.call(c1, 'browser_close', 'browser_close', {});
    await wait(1500);
    m.nodeAfterClose = await run.snap('node alone 1.5 s after browser_close', c1);
    const re = await run.call(c1, 'relaunch by navigate (ordinary page)', 'browser_navigate', { url: ORDINARY[0] });
    run.note('memory', m);
    run.note('timings', { firstNavMs: nav0.ms, closeMs: close.ms, relaunchNavMs: re.ms });
    await run.teardown(c1, 'teardown with browser open');
  } catch (e) {
    run.note('error', String(e && e.stack || e));
  } finally {
    await run.finish();
  }
}

// ------------------------------------------------------------------ D (upstream storage state)
async function scenarioD() {
  const run = new Run(name);
  const { server, origin } = await L.startPageServer();
  run.note('meta', { scenario, version, browser, runTag, origin });
  const config = makeConfig({ browser, session: run.session });
  try {
    const c1 = await run.spawnChild(version, config);
    await run.call(c1, 'navigate /a', 'browser_navigate', { url: origin + '/a' });
    await run.call(c1, 'navigate /b', 'browser_navigate', { url: origin + '/b?x=1' });
    await run.call(c1, 'write stores', 'browser_evaluate', { function: WRITE });
    const before = evalResult((await run.call(c1, 'READ before', 'browser_evaluate', { function: READ })).r);
    const saved = await run.call(c1, 'browser_storage_state (default file name)', 'browser_storage_state', {});
    const m = saved.t.match(/([A-Za-z]:\\[^\s\)\]]+storage-state[^\s\)\]]*\.json)/);
    const stateFile = m ? m[1] : null;
    let stateContent = null;
    try { stateContent = JSON.parse(fs.readFileSync(stateFile, 'utf8')); } catch (e) { stateContent = { error: String(e.message) }; }
    run.note('stateFile', { path: stateFile, content: stateContent });
    // The unclean end: the whole child and its browser go by closing the job.
    await run.kill(c1, 'UNCLEAN: job closed with the browser open');
    const c2 = await run.spawnChild(version, config);
    await run.call(c2, 'child-2 navigate /b', 'browser_navigate', { url: origin + '/b?x=1' });
    const afterKill = evalResult((await run.call(c2, 'READ after unclean end', 'browser_evaluate', { function: READ })).r);
    const restored = await run.call(c2, 'browser_set_storage_state', 'browser_set_storage_state', { filename: stateFile });
    await run.call(c2, 'reload /b', 'browser_navigate', { url: origin + '/b?x=1' });
    const afterSet = evalResult((await run.call(c2, 'READ after set_storage_state', 'browser_evaluate', { function: READ })).r);
    const cookiesAfterSet = evalResult((await run.call(c2, 'cookies after set', 'browser_run_code_unsafe', { code: COOKIES_CODE })).r);
    await run.teardown(c2, 'teardown child-2');
    // contextOptions.storageState on a persistent profile: applied or ignored?
    const fresh = path.join(run.dir, 'session-fresh');
    for (const d of ['profile', 'output', 'downloads']) fs.mkdirSync(path.join(fresh, d), { recursive: true });
    const probeState = path.join(run.dir, 'probe-state.json');
    fs.writeFileSync(probeState, JSON.stringify({ cookies: [{ name: 'fromstate', value: 'state-cookie', domain: '127.0.0.1', path: '/', expires: Math.floor(Date.now() / 1000) + 86400, httpOnly: false, secure: false, sameSite: 'Lax' }], origins: [{ origin, localStorage: [{ name: 'k', value: 'from-state-file' }] }] }));
    const cfg3 = makeConfig({ browser, session: fresh, storageState: probeState });
    run.session = fresh; // child-3 runs in the fresh session
    const c3 = await run.spawnChild(version, cfg3);
    await run.call(c3, 'child-3 navigate /c (fresh profile, storageState in contextOptions)', 'browser_navigate', { url: origin + '/c' });
    const st3 = evalResult((await run.call(c3, 'probe child-3', 'browser_evaluate', { function: `() => JSON.stringify({ cookie: document.cookie, local: localStorage.getItem('k') })` })).r);
    await run.teardown(c3, 'teardown child-3');
    run.note('storage', { before, afterKill, restoreText: restored.t.slice(0, 400), restoreIsError: isError(restored.r), afterSet, cookiesAfterSet, contextOptionsStorageStateOnPersistent: st3 });
  } catch (e) {
    run.note('error', String(e && e.stack || e));
  } finally {
    server.close();
    await run.finish();
  }
}

// ------------------------------------------------------------------ P (the preliminary answers' path: p3 + p4)
// The caller's own browser_close, then the whole child torn down (design B), then a fresh
// child at "resume" with the browser's own restore switched on through launch options
// only, then an idle-style teardown with the browser open and one more fresh child.
async function scenarioP() {
  const run = new Run(name);
  const { server, origin, requests } = await L.startPageServer();
  run.note('meta', { scenario, version, browser, runTag, origin, variant: 'sticky-restore, no about:blank start URL' });
  const config = browser === 'chromium'
    ? makeConfig({ browser, session: run.session, extraArgs: ['--restore-last-session'], ignoreDefaultArgs: ['about:blank'] })
    : makeConfig({ browser, session: run.session, ignoreDefaultArgs: ['about:blank'], firefoxPrefs: {
      'browser.sessionstore.resume_session_once': true, 'browser.sessionstore.restore_on_demand': false, 'browser.sessionstore.restore_tabs_lazily': false } });
  const short = x => x && x.url ? { url: x.url.replace(/^http:\/\/127\.0\.0\.1:\d+/, ''), title: x.title, session: x.session, form: x.formName, scrollY: x.scrollY, hist: x.historyLength, sc: x.cookie_sc, pc: x.cookie_pc, local: x.local, idb: x.idb } : String(x).slice(0, 120);
  const readAll = async (child, tag) => {
    const list = parseTabs((await run.call(child, `${tag}: tabs list`, 'browser_tabs', { action: 'list' })).t);
    const out = [];
    for (const tab of list) {
      await run.call(child, `${tag}: select ${tab.index}`, 'browser_tabs', { action: 'select', index: tab.index });
      out.push({ tab: { ...tab, url: tab.url.replace(/^http:\/\/127\.0\.0\.1:\d+/, '') }, read: short(evalResult((await run.call(child, `${tag}: READ ${tab.index}`, 'browser_evaluate', { function: READ })).r)) });
    }
    return out;
  };
  try {
    const c1 = await run.spawnChild(version, config);
    const fresh = await run.call(c1, 'FRESH SESSION first call: browser_tabs list', 'browser_tabs', { action: 'list' });
    const filled = await fillState(run, c1, origin, { withPermission: false });
    await run.call(c1, 'tabs new /b?x=2', 'browser_tabs', { action: 'new', url: origin + '/b?x=2' });
    await run.call(c1, 'submit the form by POST', 'browser_evaluate', { function: `async () => { document.getElementById('name').value = 'posted-name'; document.getElementById('f').submit(); return 'submitted'; }` });
    await wait(800);
    await run.call(c1, 'select tab 1 (/c#frag) as current', 'browser_tabs', { action: 'select', index: 1 });
    const before = await readAll(c1, 'before');
    await run.call(c1, 'reselect tab 1', 'browser_tabs', { action: 'select', index: 1 });
    const close = await run.call(c1, 'THE CALLER\'S OWN browser_close', 'browser_close', {});
    const td1 = await run.teardown(c1, 'design B: child torn down after the caller close (node only)');
    const reqMark = requests.length;
    const c2 = await run.spawnChild(version, config);
    const first = await run.call(c2, 'RESUME child-2 first call: browser_tabs list (browser launch + restore)', 'browser_tabs', { action: 'list' });
    await wait(2000);
    const restored = await readAll(c2, 'restored');
    const bTab = restored.find(t => /\/b\?x=1/.test(t.tab.url));
    let drive = null;
    if (bTab) {
      await run.call(c2, 'select restored /b tab', 'browser_tabs', { action: 'select', index: bTab.tab.index });
      const s = await run.call(c2, 'snapshot restored /b tab', 'browser_snapshot', {});
      const ref = (refsIn(s.t).find(r => r.role === 'textbox' && /Notes/i.test(r.name)) || {}).ref;
      const ty = ref ? await run.call(c2, 'type into restored tab via a fresh ref', 'browser_type', { target: ref, text: 'after-restore' }) : null;
      const back = await run.call(c2, 'navigate_back on restored /b tab', 'browser_navigate_back', {});
      drive = { snapshotOk: !isError(s.r), typeOk: ty ? !isError(ty.r) : null, backUrl: (back.t.match(/Page URL: (\S+)/) || [])[1] };
    }
    const requestsByRestore = requests.slice(reqMark).map(q => q.method + ' ' + q.path);
    // Idle-style end of child-2: torn down with the browser open, then one more resume.
    const td2 = await run.teardown(c2, 'design B: idle teardown with the browser open');
    const c3 = await run.spawnChild(version, config);
    const third = await run.call(c3, 'RESUME child-3 first call: browser_tabs list', 'browser_tabs', { action: 'list' });
    await wait(1500);
    const third2 = parseTabs((await run.call(c3, 'child-3 tabs 1.5 s later', 'browser_tabs', { action: 'list' })).t).map(t => t.url.replace(/^http:\/\/127\.0\.0\.1:\d+/, '') + (t.current ? '*' : ''));
    const td3 = await run.teardown(c3, 'final teardown');
    const c2s = run.report.steps.find(s => s.label === 'child-2 started');
    run.note('p', {
      freshFirstPage: parseTabs(fresh.t), before, closeMs: close.ms, teardownAfterCallerCloseExitMs: td1.exitMs,
      child2SpawnToInitMs: c2s && c2s.spawnToInitMs, resumeFirstCallMs: first.ms, restored, drive, requestsByRestore,
      idleTeardownBrowserOpenExitMs: td2.exitMs, child3Tabs: third2, child3FirstCallMs: third.ms, finalTeardownExitMs: td3.exitMs,
    });
  } catch (e) {
    run.note('error', String(e && e.stack || e));
  } finally {
    server.close();
    await run.finish();
  }
}

const table = { S: scenarioS, H: scenarioH, R: scenarioR, T: scenarioT, M: scenarioM, D: scenarioD, P: scenarioP };
(async () => {
  await table[scenario]();
  process.exit(0);
})().catch(e => { console.error(e); process.exit(1); });
