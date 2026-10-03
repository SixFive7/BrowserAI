// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// show.js <runName> [full] -- prints a run's steps compactly.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const ROOT = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\state-across-close';
const r = JSON.parse(fs.readFileSync(path.join(ROOT, 'runs', process.argv[2], 'report.json'), 'utf8'));
const full = process.argv[3] === 'full';
for (const s of r.steps) {
  const bits = [s.label];
  if (s.ms !== undefined) bits.push(`${s.ms} ms`);
  if (s.isError) bits.push('ISERROR');
  if (s.text) bits.push((full ? s.text : s.text.slice(0, 160)).replace(/\n/g, ' / '));
  if (s.job) bits.push(JSON.stringify(s.job));
  for (const k of ['stdinCloseToExitMs', 'stdinCloseToJobEmptyMs', 'exitCode', 'jobEmptyTimedOut', 'spawnToInitMs', 'initMs', 'pid']) if (s[k] !== undefined) bits.push(`${k}=${s[k]}`);
  if (s.har) bits.push(JSON.stringify(s.har));
  console.log('- ' + bits.join(' | '));
}
for (const k of Object.keys(r)) if (!['steps', 'name'].includes(k)) console.log(`\n## ${k}\n` + JSON.stringify(r[k], null, 1).slice(0, full ? 100000 : 4000));
