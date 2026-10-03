// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// aggregate.js -- reads every runs/*/report.json whose tag is not a pilot and writes TSV tables.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const ROOT = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\state-across-close';
const OUT = path.join(ROOT, 'results');
fs.mkdirSync(OUT, { recursive: true });
const includePilots = process.argv.includes('--pilots');
const runs = fs.readdirSync(path.join(ROOT, 'runs')).filter(d => fs.existsSync(path.join(ROOT, 'runs', d, 'report.json')))
  .map(d => ({ dir: d, r: JSON.parse(fs.readFileSync(path.join(ROOT, 'runs', d, 'report.json'), 'utf8')) }))
  .filter(x => includePilots || !/pilot|-p\d$/.test(x.dir));
const tsv = (file, header, rows) => fs.writeFileSync(path.join(OUT, file), [header.join('\t'), ...rows.map(r => r.map(v => v === undefined || v === null ? '' : String(v).replace(/\t|\n/g, ' ')).join('\t'))].join('\n') + '\n');
const r1 = x => x === undefined || x === null ? '' : Math.round(x * 10) / 10;
const step = (r, re) => r.steps.find(s => re.test(s.label));
const errors = [];
for (const { dir, r } of runs) if (r.error || !r.utcEnd) errors.push([dir, r.error || 'no utcEnd']);
tsv('errors.tsv', ['run', 'error'], errors);

// ---------------------------------------------------------------- S: survival
const S = runs.filter(x => x.r.meta && x.r.meta.scenario === 'S');
const keys = ['cookie_pc', 'cookie_jpc', 'cookie_sc', 'cookie_jsc', 'local', 'session', 'idb', 'formName', 'scrollY', 'historyLength', 'perm_geolocation', 'perm_clipboard_read'];
tsv('S-survival.tsv', ['run', 'version', 'browser', 'tabsBefore', 'tabsAfter', ...keys.flatMap(k => [k + '_before', k + '_after', k + '_designB']), 'firstCallText', 'staleClickText', 'backText'],
  S.map(({ dir, r }) => {
    const s = r.survival || {}; const b = s.before || {}, a = s.after || {}, d = (r.designB && r.designB.after) || {};
    return [dir, r.meta.version, r.meta.browser, (s.tabsBefore || []).map(t => t.url.replace(/^http:\/\/127\.0\.0\.1:\d+/, '') + (t.current ? '*' : '')).join(' '),
      (s.tabsAfter || []).map(t => t.url.replace(/^http:\/\/127\.0\.0\.1:\d+/, '') + (t.current ? '*' : '')).join(' '),
      ...keys.flatMap(k => [b[k], a[k], d[k]]), s.firstCallText, s.clickText, s.backText];
  }));

// ---------------------------------------------------------------- S: Q328 b
tsv('S-q328b-close-without-browser.tsv', ['run', 'version', 'browser', 'sample', 'ms', 'processesCreatedDuringCall', 'maxActiveInJobDuringCall', 'activeAfter', 'browserExesSeen', 'samples', 'isError', 'text'],
  S.flatMap(({ dir, r }) => ['close3', 'close4'].map(k => {
    const q = r.q328b && r.q328b[k]; if (!q) return [dir, r.meta.version, r.meta.browser, k];
    const seen = (q.watch.seen || []).filter(p => /browsers-cache/i.test(p.exe || ''));
    return [dir, r.meta.version, r.meta.browser, k, r1(q.ms), q.watch.created, q.watch.maxActive, q.watch.activeAtStop, seen.length, q.watch.samples, q.isError, q.text];
  })));

// ---------------------------------------------------------------- S: timings + memory
tsv('S-timings-memory.tsv', ['run', 'version', 'browser', 'child1SpawnToInitMs', 'firstNavMs', 'close1Ms', 'relaunchFirstCallMs', 'teardownBrowserOpenExitMs', 'teardownBrowserOpenJobEmptyMs',
  'child2SpawnToInitMs', 'child2FirstNavMs', 'close2Ms', 'teardownNodeOnlyExitMs',
  'nodeOnlyWsMB', 'nodeOnlyPrivMB', 'open2LocalTabsTotalWsMB', 'open2LocalTabsTotalPrivMB', 'open2LocalTabsBrowserProcs', 'relaunchedBlankTotalWsMB', 'relaunchedBlankBrowserProcs'],
  S.map(({ dir, r }) => {
    const t = r.timings || {}, m = r.memory || {};
    const c1 = step(r, /^child-1 started/), c2 = step(r, /^child-2 started/), fn = step(r, /^navigate \/a \(first browser/);
    return [dir, r.meta.version, r.meta.browser, r1(c1 && c1.spawnToInitMs), r1(fn && fn.ms), r1(t.close1Ms), r1(t.relaunchFirstCallMs),
      r1(t.teardownBrowserOpen && t.teardownBrowserOpen.exitMs), r1(t.teardownBrowserOpen && t.teardownBrowserOpen.toEmptyMs),
      r1(c2 && c2.spawnToInitMs), r1(t.child2FirstNavMs), r1(t.close2Ms), r1(t.teardownNodeOnly && t.teardownNodeOnly.exitMs),
      m.nodeOnly1s && m.nodeOnly1s.nodeWsMB, m.nodeOnly1s && m.nodeOnly1s.nodePrivMB, m.open2LocalTabs && m.open2LocalTabs.totalWsMB, m.open2LocalTabs && m.open2LocalTabs.totalPrivMB,
      m.open2LocalTabs && m.open2LocalTabs.browserProcs, m.relaunched && m.relaunched.totalWsMB, m.relaunched && m.relaunched.browserProcs];
  }));

// ---------------------------------------------------------------- H: HAR
const H = runs.filter(x => x.r.meta && x.r.meta.scenario === 'H');
const hs = h => h ? (h.exists ? `${h.entries}e[${(h.paths || []).join(',')}] ${h.size}B ${h.sha256}` : 'absent') : '';
tsv('H-har.tsv', ['run', 'version', 'browser', 'browser1Open', 'afterClose1', 'browser2Open', 'afterClose2', 'afterCloseWithNoBrowser', 'browser4Open', 'afterTeardownBrowser4Open', 'firstArchiveSurvivedClose2', 'close3ProcessesCreated'],
  H.map(({ dir, r }) => { const h = r.har || {}; return [dir, r.meta.version, r.meta.browser, hs(h.h0), hs(h.h1), hs(h.h2), hs(h.h3), hs(h.h4), hs(h.h5), hs(h.h6),
    h.h1 && h.h3 ? String(h.h1.sha256 === h.h3.sha256) : '', h.close3Watch && h.close3Watch.created]; }));

// ---------------------------------------------------------------- R: restore
const R = runs.filter(x => x.r.meta && x.r.meta.scenario === 'R');
const u = t => t.url.replace(/^http:\/\/127\.0\.0\.1:\d+/, '') + (t.current ? '*' : '');
tsv('R-restore.tsv', ['run', 'version', 'browser', 'variant', 'tabsBefore', 'tabsAfterRelaunch', 'firstCallMs', 'restoredB_session', 'restoredB_form', 'restoredB_scrollY', 'restoredB_history', 'restoredC_sessionCookie', 'postTab', 'driveSnapshotOk', 'driveTypeOk', 'backWentTo', 'requestsByRestore', 'cycle2Tabs(caller close, nothing armed)', 'child2Tabs(after teardown)', 'oneShotArmed'],
  R.map(({ dir, r }) => {
    const x = r.restore || {}; const cy = r.cycles || {};
    const reads = (x.perTab || []).map(p => p.read).filter(v => v && v.url);
    const b = reads.find(v => /\/b\?x=1/.test(v.url)) || {};
    const anyHttp = reads[0] || {};
    const post = reads.find(v => /\/posted/.test(v.url)) || (x.tabsLater || []).find(t => /chrome-error|posted/.test(t.url));
    const armed = r.steps.filter(s => /^ARMED/.test(s.label)).length;
    return [dir, r.meta.version, r.meta.browser, r.meta.variant, (x.tabsBefore || []).map(u).join(' '), (x.tabsLater || []).map(u).join(' '), r1(x.firstCallMs),
      b.session, b.formName, b.scrollY, b.historyLength, anyHttp.cookie_sc, post ? (post.title !== undefined ? `${post.title} ${post.url.replace(/^http:\/\/127\.0\.0\.1:\d+/, '')}` : '') : '',
      x.drive && x.drive.snapshotOk, x.drive && x.drive.typeOk, x.drive && x.drive.afterBack && x.drive.afterBack.url && x.drive.afterBack.url.replace(/^http:\/\/127\.0\.0\.1:\d+/, ''),
      (x.requestsByRestore || []).map(q => q.method + ' ' + q.path).join(' '), (cy.cycle2 || []).map(u).join(' '), (cy.child2 || []).map(u).join(' '), armed];
  }));

// ---------------------------------------------------------------- T: reopen recorded URLs
const T = runs.filter(x => x.r.meta && x.r.meta.scenario === 'T');
tsv('T-reopen.tsv', ['run', 'version', 'browser', 'recorded', 'reopened', 'urlMatches', 'titleMatches', 'currentMatches', 'reopenMs', 'perCallMs', 'upstreamCalls',
  'B_session_before', 'B_session_after', 'B_form_before', 'B_form_after', 'B_scroll_before', 'B_scroll_after', 'B_hist_before', 'B_hist_after', 'sessionCookie_after', 'post_title_before', 'post_title_after'],
  T.map(({ dir, r }) => {
    const o = r.reopen || {}; const rec = o.recorded || [], re = o.reopened || [];
    const n = Math.min(rec.length, re.length);
    let um = 0, tm = 0; for (let i = 0; i < n; i++) { if (rec[i].url === re[i].url) um++; if (rec[i].title === re[i].title) tm++; }
    const cm = rec.findIndex(t => t.current) === re.findIndex(t => t.current);
    const rb = (o.readsBefore || [])[0] || {}, ra = (o.readsAfter || [])[0] || {};
    const pb = (o.readsBefore || [])[2] || {}, pa = (o.readsAfter || [])[2] || {};
    return [dir, r.meta.version, r.meta.browser, rec.map(u).join(' '), re.map(u).join(' '), `${um}/${rec.length}`, `${tm}/${rec.length}`, cm, r1(o.reopenMs), (o.calls || []).map(c => Math.round(c.ms)).join('+') + '+' + Math.round(o.selectMs || 0), o.upstreamCalls,
      rb.session, ra.session, rb.formName, ra.formName, rb.scrollY, ra.scrollY, rb.historyLength, ra.historyLength, ra.cookie_sc, pb.title, pa.title];
  }));

// ---------------------------------------------------------------- M: memory
const M = runs.filter(x => x.r.meta && x.r.meta.scenario === 'M');
const mm = (m, k) => m && m[k] ? `${m[k].totalWsMB}/${m[k].totalPrivMB} (${m[k].browserProcs}p)` : '';
tsv('M-memory.tsv', ['run', 'version', 'browser', 'nodeBeforeBrowser WS/priv', 'open3 WS/priv', 'all3 about:blank', '1 about:blank tab', 'open3 again', 'CDP pressure+GC', 'CDP freeze', 'node after browser_close', 'firstNavMs', 'closeMs', 'relaunchNavMs'],
  M.map(({ dir, r }) => { const m = r.memory || {}; const t = r.timings || {};
    return [dir, r.meta.version, r.meta.browser, mm(m, 'nodeBeforeAnyBrowser'), mm(m, 'open3'), mm(m, 'blank3'), mm(m, 'blank1'), mm(m, 'open3again'), mm(m, 'pressure'), mm(m, 'frozen'), mm(m, 'nodeAfterClose'), r1(t.firstNavMs), r1(t.closeMs), r1(t.relaunchNavMs)]; }));

// ---------------------------------------------------------------- D: storage state
const D = runs.filter(x => x.r.meta && x.r.meta.scenario === 'D');
const ds = v => v ? ['cookie_pc', 'cookie_jpc', 'cookie_sc', 'cookie_jsc', 'local', 'session', 'idb'].map(k => `${k.replace('cookie_', '')}=${v[k] === null ? '-' : 'Y'}`).join(' ') : '';
tsv('D-storage-state.tsv', ['run', 'version', 'browser', 'fileCookies', 'fileOrigins', 'before', 'afterUncleanEnd', 'afterSetStorageState', 'setText', 'contextOptions.storageState on persistent'],
  D.map(({ dir, r }) => { const s = r.storage || {}; const f = (r.stateFile && r.stateFile.content) || {};
    return [dir, r.meta.version, r.meta.browser, (f.cookies || []).map(c => `${c.name}(${c.expires === -1 ? 'session' : 'persistent'})`).join(' '),
      (f.origins || []).map(o => `${(o.localStorage || []).length} localStorage, idb=${o.indexedDB ? o.indexedDB.length : 'absent'}`).join(' '),
      ds(s.before), ds(s.afterKill), ds(s.afterSet), (s.restoreText || '').slice(0, 120), JSON.stringify(s.contextOptionsStorageStateOnPersistent)]; }));

console.log(`runs read: ${runs.length}; S=${S.length} H=${H.length} R=${R.length} T=${T.length} M=${M.length} D=${D.length}; errors=${errors.length}`);
for (const e of errors) console.log('ERROR', e.join(' : ').slice(0, 300));
