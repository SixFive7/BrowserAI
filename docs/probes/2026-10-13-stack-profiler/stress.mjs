// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Launches the suite's Chromium the way BrowserAI launched it for the real
// screenshot arm, takes full-page captures of a 1920x16385 and a 1920x16384 page
// for most of the browser's first 30 s (Chromium's startup stack profile), and
// records every browser end with the signature of each Crashpad dump it left.
//
// Usage: node stress.mjs <outDir> <launches> <secondsPerLaunch> <on|off|alternate>
//   on   the browser as launched today
//   off  the same, plus --disable-stack-profiler
import { chromium } from 'file:///C:/Source/SixFive7/BrowserAI/.work/payload-0.0.83-registerai-0.3.0/mcp/node_modules/playwright-core/index.mjs';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const [outDir, launchesText, secondsText, order] = process.argv.slice(2);
const launches = Number(launchesText);
const seconds = Number(secondsText);
const exe = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\shot-diag-scratch\\chromium-1247\\chrome-win64\\chrome.exe';
const python = 'C:\\Program Files\\Python312\\python.exe';
const scripts = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\shot-diag-scratch\\scripts';

// The arguments BrowserAI handed Playwright for the arm's session, read from the
// kept rig's playwright-mcp-48F4177FEE82EA11.chromium-logging.json.
const browserAiArgs = [
  '--enable-automation',
  '--disable-blink-features=AutomationControlled',
  '--restore-last-session',
  '--enable-aggressive-domstorage-flushing',
  '--hide-crash-restore-bubble',
  '--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0.0.0 Safari/537.36',
  '--enable-logging=stderr',
];

const pageOf = (height) =>
  `data:text/html,<style>html,body{margin:0}</style><div style="height:${height}px;background:rgb(48,96,192)"></div>`;

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

fs.mkdirSync(outDir, { recursive: true });
const results = path.join(outDir, 'results.jsonl');

for (let i = 0; i < launches; i++) {
  const mode = order === 'alternate' ? (i % 2 === 0 ? 'on' : 'off') : order;
  const dir = path.join(outDir, `${String(i).padStart(3, '0')}-${mode}`);
  const profile = path.join(dir, 'profile');
  fs.mkdirSync(dir, { recursive: true });

  const args = mode === 'off' ? [...browserAiArgs, '--disable-stack-profiler'] : browserAiArgs;
  const record = {
    launch: i,
    mode,
    startedAt: new Date().toISOString(),
    pid: null,
    profilerThread: null,
    threadNames: null,
    captures: 0,
    error: null,
    errorAtMs: null,
    closedAtMs: null,
    dumps: [],
  };

  const t0 = Date.now();
  let context;

  try {
    context = await chromium.launchPersistentContext(profile, {
      executablePath: exe,
      headless: true,
      args,
      ignoreDefaultArgs: ['about:blank'],
      viewport: { width: 1920, height: 1080 },
      locale: 'nl-NL',
      timezoneId: 'Europe/Berlin',
      ignoreHTTPSErrors: false,
      permissions: ['clipboard-read'],
      downloadsPath: path.join(dir, 'downloads'),
    });
  } catch (failure) {
    record.error = 'launch: ' + String(failure.message).split('\n')[0];
    fs.appendFileSync(results, JSON.stringify(record) + '\n');
    continue;
  }

  context.on('close', () => {
    record.closedAtMs = Date.now() - t0;
  });

  try {
    const browser = context.browser();
    if (browser) {
      const cdp = await browser.newBrowserCDPSession();
      const info = await cdp.send('SystemInfo.getProcessInfo');
      record.pid = info.processInfo.find((process) => process.type === 'browser')?.id ?? null;
      await cdp.detach();
    }
  } catch (failure) {
    record.pidError = String(failure.message).split('\n')[0];
  }

  const page = context.pages()[0] ?? (await context.newPage());

  try {
    outer: while (Date.now() - t0 < seconds * 1000) {
      for (const height of [16385, 16384]) {
        await page.goto(pageOf(height));
        await page.screenshot({ fullPage: true, timeout: 30000 });
        record.captures++;

        if (record.pid && record.threadNames === null && Date.now() - t0 > 1500) {
          try {
            const seen = JSON.parse(execFileSync(python, ['-I', path.join(scripts, 'threadnames_live.py'), String(record.pid)]).toString());
            record.threadNames = seen.names;
            record.profilerThread = seen.names.includes('StackSamplingProfiler');
          } catch (failure) {
            record.threadNames = 'unread: ' + String(failure.message).split('\n')[0];
          }
        }

        if (Date.now() - t0 >= seconds * 1000) {
          break outer;
        }
      }
    }
  } catch (failure) {
    record.error = String(failure.message).split('\n')[0];
    record.errorAtMs = Date.now() - t0;
  }

  try {
    await context.close();
  } catch {
    // The browser may be gone already.
  }

  await sleep(1500);

  const crashpad = path.join(profile, 'Crashpad');
  const dumps = [];
  if (fs.existsSync(crashpad)) {
    for (const entry of fs.readdirSync(crashpad, { recursive: true })) {
      if (String(entry).endsWith('.dmp')) {
        dumps.push(path.join(crashpad, String(entry)));
      }
    }
  }

  if (dumps.length > 0) {
    const lines = execFileSync(python, ['-I', path.join(scripts, 'dumpsig.py'), ...dumps]).toString().trim().split('\n');
    record.dumps = lines.map((line) => JSON.parse(line));
  }

  fs.appendFileSync(results, JSON.stringify(record) + '\n');
  console.log(`launch ${i} ${mode}: captures=${record.captures} error=${record.error ?? '-'} dumps=${record.dumps.length} profilerThread=${record.profilerThread}`);

  if (record.dumps.length === 0 && record.error === null) {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

console.log('done');
