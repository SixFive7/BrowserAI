// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// mozlz4.js <file> -- decodes a Firefox mozLz40 file (sessionstore.jsonlz4) and prints a summary of windows/tabs.
'use strict';
const fs = require('node:fs');
function lz4Block(src, outLen) {
  const out = Buffer.alloc(outLen);
  let si = 0, di = 0;
  while (si < src.length) {
    const token = src[si++];
    let lit = token >> 4;
    if (lit === 15) { let b; do { b = src[si++]; lit += b; } while (b === 255); }
    src.copy(out, di, si, si + lit); si += lit; di += lit;
    if (si >= src.length) break;
    const off = src[si] | (src[si + 1] << 8); si += 2;
    let ml = token & 15;
    if (ml === 15) { let b; do { b = src[si++]; ml += b; } while (b === 255); }
    ml += 4;
    for (let k = 0; k < ml; k++) { out[di] = out[di - off]; di++; }
  }
  return out.slice(0, di);
}
const buf = fs.readFileSync(process.argv[2]);
if (buf.slice(0, 8).toString('latin1') !== 'mozLz40\0') throw new Error('not mozLz40');
const len = buf.readUInt32LE(8);
const json = JSON.parse(lz4Block(buf.slice(12), len).toString('utf8'));
const summary = {
  windows: (json.windows || []).map(w => ({ selected: w.selected, tabs: (w.tabs || []).map(t => ({ index: t.index, entries: (t.entries || []).map(e => e.url), hasFormdata: JSON.stringify(t).includes('formdata'), hasScroll: JSON.stringify(t).includes('scroll'), hasStorage: !!t.storage })) })),
  _closedWindows: (json._closedWindows || []).length,
  session: json.session,
};
console.log(JSON.stringify(summary, null, 1));
if (process.argv[3] === 'raw') console.log(JSON.stringify(json).slice(0, 4000));
