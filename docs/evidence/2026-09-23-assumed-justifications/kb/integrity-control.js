// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// POSITIVE CONTROL for every "PRAGMA integrity_check = ok" line in profile.js.
// A check that cannot see damage is indistinguishable from a store that has none.
const fs = require('fs'), path = require('path');
const { DatabaseSync } = require('node:sqlite');

function integrity(file) {
  try {
    const db = new DatabaseSync(file, { readOnly: true });
    const r = db.prepare('PRAGMA integrity_check').all(); db.close();
    return r.map(x => Object.values(x)[0]).join('; ');
  } catch (e) { return 'OPEN FAILED: ' + e.message; }
}
function rowCount(file) {
  try {
    const db = new DatabaseSync(file, { readOnly: true });
    const t = db.prepare("SELECT name FROM sqlite_master WHERE type='table'").all().map(r => r.name);
    const counts = t.map(n => { try { return n + '=' + db.prepare('SELECT COUNT(*) c FROM "' + n + '"').get().c; } catch (e) { return n + '=ERR'; } });
    db.close(); return counts.join(' ');
  } catch (e) { return 'OPEN FAILED: ' + e.message; }
}
const src = process.argv[2];
const dir = path.dirname(process.argv[3] || src);
const hdr = Buffer.alloc(100);
const fd0 = fs.openSync(src, 'r'); fs.readSync(fd0, hdr, 0, 100, 0);
const size = fs.fstatSync(fd0).size; fs.closeSync(fd0);
const pageSize = hdr.readUInt16BE(16) === 1 ? 65536 : hdr.readUInt16BE(16);
console.log('source            : ' + src);
console.log('size / page size  : ' + size + ' B / ' + pageSize + ' B  (' + (size / pageSize) + ' pages)');
console.log('undoctored        : integrity_check = ' + integrity(src));
console.log('undoctored tables : ' + rowCount(src));

function arm(label, doctor) {
  const dst = path.join(dir, 'ctl-' + label + '.sqlite');
  fs.copyFileSync(src, dst);
  const fd = fs.openSync(dst, 'r+');
  doctor(fd, fs.fstatSync(fd).size);
  fs.closeSync(fd);
  console.log('\n[' + label + '] integrity_check = ' + integrity(dst).slice(0, 300));
  console.log('[' + label + '] tables          = ' + rowCount(dst).slice(0, 200));
}

// 1. 1 KiB of 0x5A at the exact middle of the file -- the shape that came back "ok" first time.
arm('midfile-1k', (fd, sz) => fs.writeSync(fd, Buffer.alloc(1024, 0x5a), 0, 1024, Math.floor(sz / 2)));
// 2. 64 bytes inside page 2, which is the first b-tree page and is always live.
arm('page2-live', (fd) => fs.writeSync(fd, Buffer.alloc(64, 0x5a), 0, 64, pageSize + 8));
// 3. the same 64 bytes at the head of EVERY page from page 2 on.
arm('every-page', (fd, sz) => { for (let off = pageSize; off + 64 < sz; off += pageSize) fs.writeSync(fd, Buffer.alloc(64, 0x5a), 0, 64, off + 8); });
// 4. a torn tail: the file loses its last page.
arm('truncated', (fd, sz) => fs.ftruncateSync(fd, sz - pageSize));
// 5. the header's page-count field made to disagree with the file.
arm('bad-header', (fd) => { const b = Buffer.alloc(4); b.writeUInt32BE(9999, 0); fs.writeSync(fd, b, 0, 4, 28); });
