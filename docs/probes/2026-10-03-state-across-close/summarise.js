// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// summarise.js -- per cell (version x browser [x variant]) ranges and medians out of results/*.tsv
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const OUT = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\state-across-close\\results';
const read = f => { const lines = fs.readFileSync(path.join(OUT, f), 'utf8').trim().split('\n'); const h = lines[0].split('\t'); return lines.slice(1).map(l => { const c = l.split('\t'); const o = {}; h.forEach((k, i) => { o[k] = c[i]; }); return o; }); };
const med = a => { const s = a.slice().sort((x, y) => x - y); const n = s.length; return n ? (n % 2 ? s[(n - 1) / 2] : (s[n / 2 - 1] + s[n / 2]) / 2) : NaN; };
const fmt = a => { const n = a.filter(x => !isNaN(x)); if (!n.length) return 'n/a'; return `${Math.round(med(n))} [${Math.round(Math.min(...n))}-${Math.round(Math.max(...n))}] n=${n.length}`; };
const out = [];
const group = (rows, keyFn) => { const g = {}; for (const r of rows) (g[keyFn(r)] ||= []).push(r); return g; };

// timings / memory
const tm = read('S-timings-memory.tsv');
out.push('== S timings and memory: median [min-max] n');
for (const [k, rows] of Object.entries(group(tm, r => `${r.browser} ${r.version}`))) {
  out.push(`-- ${k}`);
  for (const col of Object.keys(rows[0]).filter(c => !['run', 'version', 'browser'].includes(c))) out.push(`   ${col.padEnd(34)} ${fmt(rows.map(r => Number(r[col])))}`);
}
// q328b
const q = read('S-q328b-close-without-browser.tsv');
out.push('\n== Q328 b: browser_close with no browser open');
for (const [k, rows] of Object.entries(group(q, r => `${r.browser} ${r.version}`))) {
  out.push(`-- ${k}: samples=${rows.length} ms ${fmt(rows.map(r => Number(r.ms)))} | created ${[...new Set(rows.map(r => r.processesCreatedDuringCall))].join('/')} | maxActive ${[...new Set(rows.map(r => r.maxActiveInJobDuringCall))].join('/')} | browserExesSeen ${[...new Set(rows.map(r => r.browserExesSeen))].join('/')} | activeAfter ${[...new Set(rows.map(r => r.activeAfter))].join('/')} | isError ${[...new Set(rows.map(r => r.isError))].join('/')} | text ${[...new Set(rows.map(r => r.text))].join(' || ')}`);
}
// survival tallies
const sv = read('S-survival.tsv');
out.push('\n== S survival tallies (kept = after equals before)');
const keys = ['cookie_pc', 'cookie_jpc', 'cookie_sc', 'cookie_jsc', 'local', 'session', 'idb', 'formName', 'scrollY', 'historyLength', 'perm_geolocation', 'perm_clipboard_read'];
for (const [k, rows] of Object.entries(group(sv, r => `${r.browser} ${r.version}`))) {
  out.push(`-- ${k} (n=${rows.length})`);
  for (const key of keys) {
    const after = rows.map(r => `${r[key + '_before']} -> ${r[key + '_after']}`);
    const designB = rows.map(r => r[key + '_designB']);
    const counts = {}; for (const a of after) counts[a] = (counts[a] || 0) + 1;
    const cb = {}; for (const a of designB) cb[a] = (cb[a] || 0) + 1;
    out.push(`   ${key.padEnd(20)} browser_close: ${Object.entries(counts).map(([v, c]) => `${v} x${c}`).join(' ; ')} | designB: ${Object.entries(cb).map(([v, c]) => `${v} x${c}`).join(' ; ')}`);
  }
  const ta = {}; for (const r of rows) ta[`${r.tabsBefore} -> ${r.tabsAfter}`] = (ta[`${r.tabsBefore} -> ${r.tabsAfter}`] || 0) + 1;
  out.push(`   tabs: ${Object.entries(ta).map(([v, c]) => `${v} x${c}`).join(' ; ')}`);
  const fc = {}; for (const r of rows) fc[r.firstCallText] = (fc[r.firstCallText] || 0) + 1;
  out.push(`   firstCallText: ${Object.entries(fc).map(([v, c]) => `${JSON.stringify(v)} x${c}`).join(' ; ')}`);
  const cl = {}; for (const r of rows) cl[r.staleClickText] = (cl[r.staleClickText] || 0) + 1;
  out.push(`   staleClick: ${Object.entries(cl).map(([v, c]) => `${JSON.stringify(v)} x${c}`).join(' ; ')}`);
}
// HAR
const h = read('H-har.tsv');
out.push('\n== H HAR');
for (const r of h) out.push(`   ${r.run}: close1=${r.afterClose1} | b2open=${r.browser2Open} | close2=${r.afterClose2} | closeNoBrowser=${r.afterCloseWithNoBrowser} | teardown=${r.afterTeardownBrowser4Open} | created=${r.close3ProcessesCreated}`);
// R
const R = read('R-restore.tsv');
out.push('\n== R restore');
for (const [k, rows] of Object.entries(group(R, r => `${r.browser} ${r.version} ${r.variant}`))) {
  out.push(`-- ${k} (n=${rows.length}) firstCallMs ${fmt(rows.map(r => Number(r.firstCallMs)))}`);
  for (const r of rows) out.push(`   ${r.run}: after=[${r.tabsAfterRelaunch}] B:session=${r.restoredB_session} form=${r.restoredB_form} scroll=${r.restoredB_scrollY} hist=${r.restoredB_history} sc=${r.restoredC_sessionCookie} post=${r.postTab} drive=${r.driveSnapshotOk}/${r.driveTypeOk} back=${r.backWentTo} | cycle2=[${r['cycle2Tabs(caller close, nothing armed)']}] | child2=[${r['child2Tabs(after teardown)']}] armed=${r.oneShotArmed}`);
}
// T
const T = read('T-reopen.tsv');
out.push('\n== T reopen recorded URLs');
for (const [k, rows] of Object.entries(group(T, r => `${r.browser} ${r.version}`))) {
  out.push(`-- ${k} (n=${rows.length}) reopenMs ${fmt(rows.map(r => Number(r.reopenMs)))}`);
  for (const r of rows) out.push(`   ${r.run}: url ${r.urlMatches} title ${r.titleMatches} current ${r.currentMatches} calls ${r.perCallMs} | B session ${r.B_session_before}->${r.B_session_after} form ${r.B_form_before}->${r.B_form_after} scroll ${r.B_scroll_before}->${r.B_scroll_after} hist ${r.B_hist_before}->${r.B_hist_after} sc_after=${r.sessionCookie_after} post ${r.post_title_before}->${r.post_title_after}`);
}
// M
const M = read('M-memory.tsv');
out.push('\n== M memory (total WS MB / total private MB (browser procs))');
for (const r of M) out.push(`   ${r.run}: node0=${r['nodeBeforeBrowser WS/priv']} open3=${r['open3 WS/priv']} blank3=${r['all3 about:blank']} blank1=${r['1 about:blank tab']} again=${r['open3 again']} pressure=${r['CDP pressure+GC']} freeze=${r['CDP freeze']} nodeAfterClose=${r['node after browser_close']} | nav ${r.firstNavMs} close ${r.closeMs} relaunch ${r.relaunchNavMs}`);
// D
const D = read('D-storage-state.tsv');
out.push('\n== D storage state');
for (const r of D) out.push(`   ${r.run}: file=[${r.fileCookies}] [${r.fileOrigins}] | before ${r.before} | unclean ${r.afterUncleanEnd} | afterSet ${r.afterSetStorageState} | ctxOpt ${r['contextOptions.storageState on persistent']}`);
fs.writeFileSync(path.join(OUT, 'summary.txt'), out.join('\n') + '\n');
console.log(out.join('\n'));
