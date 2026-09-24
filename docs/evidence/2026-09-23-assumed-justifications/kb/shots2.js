// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Follow-up: if a static page IS byte-stable, what is it that actually moves?
const { chromium } = require(process.env.CORE_PATH);
const crypto = require('crypto');
const sha = b => crypto.createHash('sha256').update(b).digest('hex');
const STATIC = 'data:text/html,<html><body style="margin:0"><h1>ok</h1></body></html>';
const CLOCK  = 'data:text/html,<html><body style="margin:0"><h1 id=t></h1><script>document.getElementById("t").textContent=String(performance.now())<\/script></body></html>';
(async () => {
  const shot = async (exe, url, opts = {}) => {
    const b = await chromium.launch({ executablePath: exe, headless: true, timeout: 60000 });
    const ctx = await b.newContext({ viewport: { width: 800, height: 600 }, deviceScaleFactor: 1 });
    const p = await ctx.newPage(); await p.goto(url);
    const buf = await p.screenshot({ type: 'png', ...opts });
    await b.close(); return { len: buf.length, sha: sha(buf) };
  };
  const FULL = process.env.CHROME_EXE, SHELL = process.env.SHELL_EXE;
  const a = await shot(FULL, STATIC), b = await shot(SHELL, STATIC);
  console.log(`full  chrome, static page : ${a.len} B ${a.sha}`);
  console.log(`headless shell, static page: ${b.len} B ${b.sha}`);
  console.log(`same binary? no -- identical bytes: ${a.sha === b.sha}\n`);
  const c1 = await shot(FULL, CLOCK), c2 = await shot(FULL, CLOCK);
  console.log(`full chrome, page whose CONTENT varies, capture 1: ${c1.len} B ${c1.sha}`);
  console.log(`full chrome, page whose CONTENT varies, capture 2: ${c2.len} B ${c2.sha}`);
  console.log(`identical bytes: ${c1.sha === c2.sha}\n`);
  const f1 = await shot(FULL, STATIC, { fullPage: true }), f2 = await shot(FULL, STATIC, { fullPage: true });
  console.log(`fullPage capture 1: ${f1.len} B ${f1.sha}`);
  console.log(`fullPage capture 2: ${f2.len} B ${f2.sha}`);
  console.log(`identical bytes: ${f1.sha === f2.sha}`);
})().catch(e => { console.error('PROBE FAILED: ' + e.message); process.exit(1); });
