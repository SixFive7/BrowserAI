// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch: prints the aggregate TSV compactly, skipping the setup steps.
// The aggregate's first column is "scenario family step" joined by spaces.
'use strict';
const fs = require('fs');
const [AGG, FILTER] = process.argv.slice(2);
const lines = fs.readFileSync(AGG, 'utf8').trim().split('\n').slice(1);
const skip = new Set(['init', 'toolsList', 'navigateStart', 'markers']);
for (const l of lines) {
  const [key, n, answered, isError, paused, lat, texts] = l.split('\t');
  const [scenario, family, step] = key.split(' ');
  if (skip.has(step)) continue;
  if (FILTER && !new RegExp(FILTER).test(key)) continue;
  const t = (texts || '').slice(0, 120);
  console.log(`${scenario.padEnd(16)} ${family.padEnd(8)} ${step.padEnd(34)} n=${n} answered=${answered} err=${isError} paused=${paused} ms=${lat || '-'}${t ? '  :: ' + t : ''}`);
}
