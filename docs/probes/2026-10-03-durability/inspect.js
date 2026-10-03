// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Scratch rig (durability research, 2026-10-03). Reads, after a kill and before any relaunch,
// what the killed browser left on disk for the stores that are files a person can read:
// Chromium's Default\Preferences and Default\Sessions, Firefox's session-store files.
// Usage: node inspect.js <browser> <profile> <run> [--patch-exit-type]  -> one JSON line.
// --patch-exit-type rewrites Chromium's profile.exit_type "Crashed" to "Normal" (a lever under test).
'use strict';
const fs = require('node:fs');
const path = require('node:path');

const [browser, profile, run, flag] = process.argv.slice(2);
const out = { browser };

function lz4Block(src, outLen) {
  const o = Buffer.alloc(outLen);
  let si = 0, di = 0;
  while (si < src.length) {
    const token = src[si++];
    let lit = token >> 4;
    if (lit === 15) { let b; do { b = src[si++]; lit += b; } while (b === 255); }
    src.copy(o, di, si, si + lit); si += lit; di += lit;
    if (si >= src.length) break;
    const off = src[si] | (src[si + 1] << 8); si += 2;
    let ml = token & 15;
    if (ml === 15) { let b; do { b = src[si++]; ml += b; } while (b === 255); }
    ml += 4;
    for (let k = 0; k < ml; k++) { o[di] = o[di - off]; di++; }
  }
  return o.slice(0, di);
}

function readMozLz4(file) {
  const buf = fs.readFileSync(file);
  if (buf.slice(0, 8).toString('latin1') !== 'mozLz40\0') return { error: 'not mozLz40' };
  return JSON.parse(lz4Block(buf.slice(12), buf.readUInt32LE(8)).toString('utf8'));
}

function tokensIn(buf) {
  // Which /tab?run=<run>&k=<n> and /write?run=<run> URLs a binary file mentions, UTF-8 or UTF-16LE.
  const found = [];
  for (const enc of ['utf8', 'utf16le']) {
    const s = buf.toString(enc);
    const re = new RegExp('/(tab|write)\\?run=' + run.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '(&k=(\\d+))?', 'g');
    let m;
    while ((m = re.exec(s))) found.push(m[1] === 'tab' ? 'tab' + m[3] : 'write');
  }
  return [...new Set(found)].sort();
}

try {
  if (browser === 'chromium') {
    const prefsFile = path.join(profile, 'Default', 'Preferences');
    if (fs.existsSync(prefsFile)) {
      const raw = fs.readFileSync(prefsFile, 'utf8');
      const j = JSON.parse(raw);
      out.prefsBytes = raw.length;
      out.prefsMtime = fs.statSync(prefsFile).mtimeMs;
      out.bookmarkBarShowOnAllTabs = j.bookmark_bar ? j.bookmark_bar.show_on_all_tabs : undefined;
      out.exitType = j.profile ? j.profile.exit_type : undefined;
      if (flag === '--patch-exit-type' && j.profile && j.profile.exit_type === 'Crashed') {
        j.profile.exit_type = 'Normal';
        fs.writeFileSync(prefsFile, JSON.stringify(j));
        out.patchedExitType = 'Crashed->Normal';
      }
    } else out.prefs = 'absent';
    const sdir = path.join(profile, 'Default', 'Sessions');
    out.sessionFiles = [];
    if (fs.existsSync(sdir)) {
      for (const f of fs.readdirSync(sdir)) {
        const p = path.join(sdir, f);
        const buf = fs.readFileSync(p);
        out.sessionFiles.push({ f, bytes: buf.length, mtime: fs.statSync(p).mtimeMs, tokens: tokensIn(buf) });
      }
    }
    out.sessionTokens = [...new Set(out.sessionFiles.filter(x => x.f.startsWith('Session_')).flatMap(x => x.tokens))].sort();
  } else {
    const files = ['sessionstore.jsonlz4', 'sessionstore-backups/recovery.jsonlz4', 'sessionstore-backups/recovery.baklz4', 'sessionstore-backups/previous.jsonlz4'];
    out.sessionFiles = [];
    for (const rel of files) {
      const p = path.join(profile, rel);
      if (!fs.existsSync(p)) continue;
      const rec = { f: rel, bytes: fs.statSync(p).size, mtime: fs.statSync(p).mtimeMs };
      try {
        const j = readMozLz4(p);
        rec.tabs = (j.windows || []).flatMap(w => (w.tabs || []).map(t => { const e = (t.entries || [])[(t.index || 1) - 1] || {}; return e.url || ''; }));
        rec.tokens = tokensIn(Buffer.from(JSON.stringify(j), 'utf8'));
        rec.state = j.session && j.session.state;
      } catch (e) { rec.error = String(e && e.message || e); }
      out.sessionFiles.push(rec);
    }
    const rec = out.sessionFiles.find(x => x.f === 'sessionstore-backups/recovery.jsonlz4');
    out.sessionTokens = rec && rec.tokens ? rec.tokens : [];
  }
} catch (e) { out.error = String(e && e.stack || e); }
process.stdout.write(JSON.stringify(out) + '\n');
