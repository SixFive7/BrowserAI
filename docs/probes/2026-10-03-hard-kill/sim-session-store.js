// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch rig: what a hard kill leaves in a session's record, simulated with the schema
// SessionStore.Create writes (src/BrowserAI/Storage/SessionStore.cs) -- NOT with BrowserAI.
// mode "hold": create the store in WAL, write an acquisition and one settled and one
// in-flight log row, print READY and hold the connection open until killed.
// mode "read": open the store afterwards and print what a reader sees.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { DatabaseSync } = require('node:sqlite');

const [mode, dir] = process.argv.slice(2);
const file = path.join(dir, 'browserai.data');

function listing() {
  return fs.readdirSync(dir).sort().map(n => `${n}:${fs.statSync(path.join(dir, n)).size}`);
}

if (mode === 'hold') {
  fs.mkdirSync(dir, { recursive: true });
  const db = new DatabaseSync(file);
  const jm = db.prepare('PRAGMA journal_mode=WAL').get();
  db.exec(`
    CREATE TABLE IF NOT EXISTS statements (field TEXT NOT NULL, at TEXT NOT NULL, value TEXT NOT NULL);
    CREATE TABLE IF NOT EXISTS log (id INTEGER PRIMARY KEY, at TEXT NOT NULL, tool TEXT NOT NULL, why TEXT NOT NULL,
      outcome TEXT NOT NULL, settled_at TEXT, failure BLOB);
    PRAGMA user_version = 1;`);
  const now = () => new Date().toISOString();
  db.exec('BEGIN IMMEDIATE');
  const st = db.prepare('INSERT INTO statements (field, at, value) VALUES (?, ?, ?)');
  st.run('holder', now(), `{"pid":${process.pid}}`);
  st.run('purpose', now(), 'hard-kill simulation');
  db.exec('COMMIT');
  const ins = db.prepare("INSERT INTO log (at, tool, why, outcome, settled_at, failure) VALUES (?, ?, ?, ?, NULL, NULL)");
  const a = ins.run(now(), 'browser_navigate', 'a call that was answered', 'in-flight').lastInsertRowid;
  db.prepare('UPDATE log SET outcome = ?, settled_at = ?, failure = ? WHERE id = ?').run('successful', now(), null, a);
  ins.run(now(), 'browser_click', 'a call still in flight when the process died', 'in-flight');
  process.stdout.write('READY ' + JSON.stringify({ pid: process.pid, journal: jm, files: listing() }) + '\n');
  setInterval(() => {}, 1 << 30);
} else {
  const before = listing();
  const db = new DatabaseSync(file, { readOnly: true });
  const rows = db.prepare('SELECT id, tool, outcome, settled_at FROM log ORDER BY id').all();
  const integrity = db.prepare('PRAGMA integrity_check').get();
  db.close();
  process.stdout.write('READ ' + JSON.stringify({ before, after: listing(), rows, integrity }) + '\n');
}
