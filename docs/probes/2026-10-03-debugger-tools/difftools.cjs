// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch: diffs two tools/list dumps (and the golden snapshot) by name,
// description, title, annotations and input schema. Writes a TSV and a JSON.
'use strict';
const fs = require('fs');
const [A, B, GOLDEN, OUT] = process.argv.slice(2);
const load = (f) => { const j = JSON.parse(fs.readFileSync(f, 'utf8')); return j.tools || (j.result && j.result.tools) || j; };
const a = load(A), b = load(B), g = GOLDEN ? load(GOLDEN) : null;
const byName = (list) => new Map(list.map((t) => [t.name, t]));
const ma = byName(a), mb = byName(b);
const rows = [];
const all = [...new Set([...a.map((t) => t.name), ...b.map((t) => t.name)])];
for (const n of all) {
  const x = ma.get(n), y = mb.get(n);
  if (!x) { rows.push({ name: n, change: 'added', detail: y.description }); continue; }
  if (!y) { rows.push({ name: n, change: 'removed', detail: x.description }); continue; }
  const diffs = [];
  if (x.description !== y.description) diffs.push({ field: 'description', from: x.description, to: y.description });
  if (JSON.stringify(x.annotations) !== JSON.stringify(y.annotations)) diffs.push({ field: 'annotations', from: x.annotations, to: y.annotations });
  if (JSON.stringify(x.inputSchema) !== JSON.stringify(y.inputSchema)) diffs.push({ field: 'inputSchema', from: x.inputSchema, to: y.inputSchema });
  if (diffs.length) rows.push({ name: n, change: 'changed', diffs });
}
const orderA = a.map((t) => t.name).join(','), orderB = b.map((t) => t.name).join(',');
let goldenVsA = null;
if (g) {
  const mg = byName(g);
  goldenVsA = { goldenCount: g.length, liveCount: a.length, sameNames: g.map((t) => t.name).join(',') === orderA,
    differing: a.filter((t) => { const h = mg.get(t.name); return !h || JSON.stringify(h) !== JSON.stringify(t); }).map((t) => t.name) };
}
const out = { a: { file: A, count: a.length }, b: { file: B, count: b.length }, sameOrder: orderA === orderB, rows, goldenVsA };
fs.writeFileSync(OUT, JSON.stringify(out, null, 2));
const tsv = ['name\tchange\tfield\tfrom\tto'];
for (const r of rows) {
  if (r.change !== 'changed') tsv.push(`${r.name}\t${r.change}\t\t\t${String(r.detail).replace(/\s+/g, ' ')}`);
  else for (const d of r.diffs) tsv.push(`${r.name}\tchanged\t${d.field}\t${JSON.stringify(d.from).slice(0, 400)}\t${JSON.stringify(d.to).slice(0, 400)}`);
}
fs.writeFileSync(OUT.replace(/\.json$/, '.tsv'), tsv.join('\n') + '\n');
console.log(JSON.stringify({ countA: a.length, countB: b.length, sameOrder: out.sameOrder, changes: rows.length, goldenVsA }, null, 2));
