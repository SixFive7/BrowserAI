// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Probe: are real screenshots byte-stable across captures and across runs?
const { chromium } = require(process.env.CORE_PATH);
const crypto = require('crypto');
const EXE = process.env.CHROME_EXE;
const PAGE = 'data:text/html,<html><body style="margin:0;background:%23fff"><h1>ok</h1></body></html>';
const OTHER = 'data:text/html,<html><body style="margin:0;background:%23fff"><h1>ok.</h1></body></html>';
const sha = b => crypto.createHash('sha256').update(b).digest('hex');
(async () => {
  const out = [];
  for (const run of [1, 2]) {
    const b = await chromium.launch({ executablePath: EXE, headless: true, timeout: 60000 });
    const ctx = await b.newContext({ viewport: { width: 800, height: 600 }, deviceScaleFactor: 1 });
    const p = await ctx.newPage();
    await p.goto(PAGE);
    for (let i = 1; i <= 3; i++) {
      const buf = await p.screenshot({ type: 'png' });
      out.push({ run, shot: i, bytes: buf.length, sha: sha(buf) });
    }
    if (run === 2) {
      await p.goto(OTHER);
      const buf = await p.screenshot({ type: 'png' });
      out.push({ run: 'CONTROL', shot: 'different-page', bytes: buf.length, sha: sha(buf) });
    }
    await b.close();
  }
  for (const r of out) console.log(`run ${r.run} shot ${r.shot}  ${String(r.bytes).padStart(7)} B  ${r.sha}`);
  const real = out.filter(r => r.run !== 'CONTROL');
  const distinct = new Set(real.map(r => r.sha));
  console.log(`\ndistinct hashes over ${real.length} captures of the SAME page: ${distinct.size}`);
  console.log(`within run 1 identical: ${new Set(real.filter(r=>r.run===1).map(r=>r.sha)).size === 1}`);
  console.log(`within run 2 identical: ${new Set(real.filter(r=>r.run===2).map(r=>r.sha)).size === 1}`);
  console.log(`run1 == run2          : ${real.find(r=>r.run===1).sha === real.find(r=>r.run===2).sha}`);
  const ctl = out.find(r => r.run === 'CONTROL');
  console.log(`CONTROL (different page) differs from run 2: ${ctl.sha !== real.find(r=>r.run===2).sha}`);
})().catch(e => { console.error('PROBE FAILED: ' + e.message); process.exit(1); });
