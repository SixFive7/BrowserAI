// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Compares two user-path readings: whether B is exactly A with ";<entry>" appended, and
// whether two readings are byte-identical (raw registry bytes and kind).
// usage: node q304-pathdiff.js <a.json> <b.json> [entry]
'use strict';
const fs = require('fs');
const [a, b, entry] = process.argv.slice(2).map((x, i) => (i < 2 ? JSON.parse(fs.readFileSync(x, 'utf8')) : x));
const rawA = fs.readFileSync(process.argv[2] + '.raw');
const rawB = fs.readFileSync(process.argv[3] + '.raw');
const out = {
  a: { at: a.at, regType: a.regType, rawBytes: a.rawBytes, rawSha256: a.rawSha256, chars: a.chars, textUtf16Sha256: a.textUtf16Sha256 },
  b: { at: b.at, regType: b.regType, rawBytes: b.rawBytes, rawSha256: b.rawSha256, chars: b.chars, textUtf16Sha256: b.textUtf16Sha256 },
  rawBytesIdentical: Buffer.compare(rawA, rawB) === 0,
  kindIdentical: a.regType === b.regType && a.kind === b.kind,
};
if (entry) {
  out.entry = entry;
  out.bIsAPlusSeparatorPlusEntry = b.text === `${a.text};${entry}`;
  out.aEndsWithSeparator = a.text.endsWith(';');
}
console.log(JSON.stringify(out, null, 1));
