// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Phase F: the cost curve of ServerRegistry.list() against registry size.
// Single process, one size per run, timed in two halves (chokidar ready, then
// the rest of list()). Entirely inside PWTEST_SERVER_REGISTRY; the real
// directory is only ever read.
const fs = require('fs');
const path = require('path');
const REPO = 'C:/Source/SixFive7/BrowserAI';
const PWLIB = path.join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core');
const { serverRegistry } = require(path.join(PWLIB, 'lib', 'serverRegistry.js'));

const REAL = path.join(process.env.LOCALAPPDATA, 'ms-playwright', 'b');
const REG = process.env.PWTEST_SERVER_REGISTRY;
const N = Number(process.argv[2]);

(async () => {
  const realStart = fs.readdirSync(REAL).length;
  fs.rmSync(REG, { recursive: true, force: true });
  fs.mkdirSync(REG, { recursive: true });
  const all = fs.readdirSync(REAL).slice(0, N);
  for (const f of all) fs.copyFileSync(path.join(REAL, f), path.join(REG, f));
  const seeded = fs.readdirSync(REG).length;

  const t0 = Date.now();
  const dispose = serverRegistry.watch();
  await serverRegistry.ready();
  const ready = Date.now() - t0;
  dispose();

  const t1 = Date.now();
  await serverRegistry.list();
  const listMs = Date.now() - t1;
  const left = fs.readdirSync(REG).length;

  console.log(JSON.stringify({
    seeded,
    readyMs: ready,
    readyPerEntry: +(ready / seeded).toFixed(3),
    listMs,
    listPerEntry: +(listMs / seeded).toFixed(3),
    reaped: seeded - left,
    realBefore: realStart,
    realAfter: fs.readdirSync(REAL).length,
  }));
})().then(() => process.exit(0), e => { console.error('FATAL', e); process.exit(1); });
