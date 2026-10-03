// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch: flattens every runs/<mcp>/<scenario>/<family>/<n>/result.json into
// one row per step, then aggregates per (scenario, family, step) across runs.
// Usage: node summarize.cjs <runsRoot> <outPrefix>
'use strict';
const fs = require('fs');
const path = require('path');
const [ROOT, OUT] = process.argv.slice(2);
const rows = [];
const runs = [];
for (const scenario of fs.readdirSync(ROOT)) {
  const sd = path.join(ROOT, scenario);
  if (!fs.statSync(sd).isDirectory()) continue;
  for (const family of fs.readdirSync(sd)) {
    const fd = path.join(sd, family);
    if (!fs.statSync(fd).isDirectory()) continue;
    for (const run of fs.readdirSync(fd)) {
      const f = path.join(fd, run, 'result.json');
      if (run.includes('partial') || !fs.existsSync(f)) continue;
      const j = JSON.parse(fs.readFileSync(f, 'utf8'));
      if (!j.outcome) continue;
      runs.push({ scenario, family, run, outcome: (j.outcome || '').split('\n')[0], versions: j.versions, childExitedOnStdinEnd: j.childExitedOnStdinEnd, http: (j.http || []).map((h) => `${h.path}@${h.relMs}`).join(' ') });
      // One level of nesting is flattened into dotted step names, so that
      // final.navigate, final.markers and closeAtEnd.close are rows of their own.
      const entries = [];
      for (const [step, v] of Object.entries(j.steps || {})) {
        const nested = v && typeof v === 'object' && !Array.isArray(v) && Object.values(v).some((x) => x && typeof x === 'object' && !Array.isArray(x) && ('answered' in x));
        if (nested) {
          for (const [k2, v2] of Object.entries(v)) entries.push([`${step}.${k2}`, (v2 && typeof v2 === 'object' && !Array.isArray(v2)) ? v2 : { text: v2 }]);
        } else entries.push([step, v]);
      }
      for (const [step, v] of entries) {
        const flat = (v && typeof v === 'object' && !Array.isArray(v)) ? v : { value: v, text: Array.isArray(v) ? JSON.stringify(v) : v };
        rows.push({ scenario, family, run, step,
          answered: flat.answered ?? flat.ok ?? '',
          latencyMs: flat.latencyMs ?? '',
          isError: flat.isError ?? '',
          paused: flat.paused ?? flat.snapshotShowsPaused ?? '',
          extra: JSON.stringify(Object.fromEntries(Object.entries(flat).filter(([k]) => !['answered', 'latencyMs', 'isError', 'paused', 'text'].includes(k)))).slice(0, 600),
          text: String(flat.text ?? (Array.isArray(v) ? JSON.stringify(v) : '')).slice(0, 300) });
      }
    }
  }
}
const esc = (s) => String(s).replace(/[\t\r\n]+/g, ' ');
fs.writeFileSync(OUT + '-steps.tsv', ['scenario\tfamily\trun\tstep\tanswered\tlatencyMs\tisError\tpaused\textra\ttext', ...rows.map((r) => [r.scenario, r.family, r.run, r.step, r.answered, r.latencyMs, r.isError, r.paused, r.extra, r.text].map(esc).join('\t'))].join('\n') + '\n');
fs.writeFileSync(OUT + '-runs.tsv', ['scenario\tfamily\trun\toutcome\tmcp\tchildExitedOnStdinEnd\thttp', ...runs.map((r) => [r.scenario, r.family, r.run, r.outcome, r.versions && r.versions.mcp, r.childExitedOnStdinEnd, r.http].map(esc).join('\t'))].join('\n') + '\n');
const groups = new Map();
for (const r of rows) {
  const k = `${r.scenario}\t${r.family}\t${r.step}`;
  if (!groups.has(k)) groups.set(k, []);
  groups.get(k).push(r);
}
const agg = ['scenario\tfamily\tstep\tn\tanswered\tisError\tpaused\tlatencyMs(min/median/max)\tdistinctTexts'];
for (const [k, list] of groups) {
  const lat = list.map((r) => r.latencyMs).filter((x) => x !== '' && x !== null).map(Number).sort((a, b) => a - b);
  const med = lat.length ? lat[Math.floor((lat.length - 1) / 2)] : '';
  const texts = [...new Set(list.map((r) => r.text.replace(/\d{2,}/g, '#').replace(/127\.0\.0\.1:#/g, 'HOST').slice(0, 160)))];
  agg.push([k, list.length, list.filter((r) => r.answered === true).length, list.filter((r) => r.isError === true).length, list.filter((r) => r.paused === true).length, lat.length ? `${lat[0]}/${med}/${lat[lat.length - 1]}` : '', texts.join(' || ')].map(esc).join('\t'));
}
fs.writeFileSync(OUT + '-agg.tsv', agg.join('\n') + '\n');
console.log(`runs=${runs.length} steps=${rows.length} groups=${groups.size}`);
