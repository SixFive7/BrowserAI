// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// Scratch rig: PRAGMA integrity_check over every SQLite file in a profile, run on COPIES
// (each with its -wal / -journal / -shm siblings) so the profile itself is never opened.
// Usage: node integrity.js <profileDir> <scratchCopyDir>   -> one JSON line on stdout.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { DatabaseSync } = require('node:sqlite');

const [profile, copyRoot] = process.argv.slice(2);
const MAGIC = Buffer.from('SQLite format 3\0', 'latin1');
const SIDECARS = ['-wal', '-journal', '-shm'];
const COPIED = ['-wal', '-journal']; // a stale -shm is rebuilt by SQLite from the -wal

function* walk(dir) {
  let entries;
  try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return; }
  for (const e of entries) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) yield* walk(p);
    else if (e.isFile()) yield p;
  }
}

function isSqlite(file) {
  if (SIDECARS.some(s => file.endsWith(s))) return false;
  let fd;
  try {
    fd = fs.openSync(file, 'r');
    const b = Buffer.alloc(16);
    const n = fs.readSync(fd, b, 0, 16, 0);
    return n === 16 && b.equals(MAGIC);
  } catch { return false; } finally { if (fd !== undefined) fs.closeSync(fd); }
}

const results = [];
fs.mkdirSync(copyRoot, { recursive: true });
let i = 0;
for (const file of walk(profile)) {
  if (!isSqlite(file)) continue;
  const rel = path.relative(profile, file);
  const dir = path.join(copyRoot, String(i++));
  fs.mkdirSync(dir, { recursive: true });
  const target = path.join(dir, path.basename(file));
  const r = { file: rel, bytes: fs.statSync(file).size, sidecars: [] };
  try {
    fs.copyFileSync(file, target);
    for (const s of SIDECARS) {
      if (fs.existsSync(file + s)) {
        if (COPIED.includes(s)) fs.copyFileSync(file + s, target + s);
        r.sidecars.push(s + ':' + fs.statSync(file + s).size);
      }
    }
    const db = new DatabaseSync(target);
    const rows = db.prepare('PRAGMA integrity_check').all();
    r.integrity = rows.map(x => Object.values(x)[0]).join('; ');
    db.close();
  } catch (e) {
    r.integrity = 'ERROR: ' + String(e && e.message || e);
  }
  results.push(r);
}
fs.rmSync(copyRoot, { recursive: true, force: true });
const bad = results.filter(r => r.integrity !== 'ok');
process.stdout.write(JSON.stringify({ count: results.length, ok: results.length - bad.length, bad, all: results }) + '\n');
