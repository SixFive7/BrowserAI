// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The reproduction for the report to Playwright, through playwright-core alone: a persistent
// Firefox profile is given one page, closed, and launched again after the page's server has
// ended, with the session restore on and Playwright's blank page left out. Beside it the
// same launch with the blank page kept, with the server still answering and with the page
// cacheable, and, with no restore at all, a failed navigation followed by a wait for 'load',
// in Firefox and in Chromium. The script the report carries is the first variant with the
// import written as `import { firefox } from 'playwright-core'`; here the package is the
// payload's, named by --playwright-core.

import { mkdir, writeFile } from 'node:fs/promises';
import { createServer } from 'node:http';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { EventLog, iso, parseArgs, required, waitForFile } from '../2026-10-13-restore-stall/lib.mjs';

const args = required(parseArgs(process.argv.slice(2)), 'playwright-core', 'out', 'go');
await waitForFile(args.go);
await mkdir(args.out, { recursive: true });

const { firefox, chromium } = await import(pathToFileURL(join(args['playwright-core'], 'index.mjs')).href);
const log = new EventLog(join(args.out, 'events.jsonl'));
const TIMEOUT = 30000;

function serve(cacheControl) {
  const server = createServer((request, response) => {
    response.writeHead(200, { 'content-type': 'text/html', ...(cacheControl ? { 'cache-control': cacheControl } : {}) });
    response.end('<title>restored</title><p>restored</p>');
  });
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve(server)));
}

async function restoreVariant(name, { keepBlank, serverEnds, cacheControl }) {
  const server = await serve(cacheControl);
  const url = `http://127.0.0.1:${server.address().port}/`;
  const userDataDir = join(args.out, name, 'profile');
  await mkdir(userDataDir, { recursive: true });
  const options = {
    headless: true,
    firefoxUserPrefs: { 'browser.sessionstore.resume_session_once': true },
    ...(keepBlank ? {} : { ignoreDefaultArgs: ['about:blank'] }),
  };
  const row = { name, keepBlank, serverEnds, cacheControl: cacheControl ?? null, url };

  // First launch: open the page and close cleanly, so Firefox saves the session. The
  // preference reaches Firefox at run time, so it is in prefs.js for the second launch.
  let context = await firefox.launchPersistentContext(userDataDir, options);
  const page = context.pages()[0] ?? await context.newPage();
  await page.goto(url);
  await context.close();

  if (serverEnds) await new Promise((resolve) => server.close(resolve));

  // Second launch on the same profile.
  const started = Date.now();
  try {
    context = await firefox.launchPersistentContext(userDataDir, { ...options, timeout: TIMEOUT });
    row.outcome = 'launched';
    row.ms = Date.now() - started;
    row.pages = context.pages().map((each) => each.url());
    await context.close();
  } catch (error) {
    row.outcome = String(error.message).split('\n')[0];
    row.ms = Date.now() - started;
  }
  if (!serverEnds) await new Promise((resolve) => server.close(resolve));
  log.write('variant', row);
  return row;
}

async function loadAfterAFailedNavigation(name, browserType) {
  const server = await serve('no-store');
  const url = `http://127.0.0.1:${server.address().port}/`;
  await new Promise((resolve) => server.close(resolve));
  // Chromium's full build, the one BrowserAI launches; the scratch root holds no headless shell.
  const browser = await browserType.launch(browserType === chromium ? { headless: true, channel: 'chromium' } : { headless: true });
  const row = { name, url };
  try {
    const page = await browser.newPage();
    try { await page.goto(url); row.goto = 'resolved'; } catch (error) { row.goto = String(error.message).split('\n')[0]; }
    const started = Date.now();
    try {
      await page.waitForLoadState('load', { timeout: TIMEOUT });
      row.load = 'resolved';
    } catch (error) {
      row.load = String(error.message).split('\n')[0];
    }
    row.loadMs = Date.now() - started;
    row.pageUrl = page.url();
  } finally {
    await browser.close();
  }
  log.write('variant', row);
  return row;
}

const rows = [];
rows.push(await restoreVariant('restore, blank page left out, no-store, server ended', { keepBlank: false, serverEnds: true, cacheControl: 'no-store' }));
rows.push(await restoreVariant('restore, blank page kept, no-store, server ended', { keepBlank: true, serverEnds: true, cacheControl: 'no-store' }));
rows.push(await restoreVariant('restore, blank page left out, no-store, server answering', { keepBlank: false, serverEnds: false, cacheControl: 'no-store' }));
rows.push(await restoreVariant('restore, blank page left out, cacheable, server ended', { keepBlank: false, serverEnds: true, cacheControl: null }));
rows.push(await loadAfterAFailedNavigation('firefox: goto a refused address, then wait for load', firefox));
rows.push(await loadAfterAFailedNavigation('chromium: goto a refused address, then wait for load', chromium));

await writeFile(join(args.out, 'result.json'), `${JSON.stringify({ finished: iso(), rows }, null, 1)}\n`);
await log.close();
process.exit(0);
