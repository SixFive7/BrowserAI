// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Written after the repro stage, whose goto to a refused address left the page on about:blank
// in both browsers, so a wait for 'load' after it said nothing about an error page. Here the
// page navigates itself to a refused address, which is a navigation Firefox shows its error
// page for, as it does for a restored tab, and then the same wait for 'load', in Firefox and in
// Chromium, through playwright-core alone and with every launch option at its default. It
// answers whether the missing 'load' of the restore reaches a launch that leaves nothing out.

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
const TIMEOUT = 15000;

function listen(handler) {
  const server = createServer(handler);
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve(server)));
}

async function variant(name, browserType, options) {
  const live = await listen((request, response) => {
    response.writeHead(200, { 'content-type': 'text/html' });
    response.end('<title>live</title><p>live</p>');
  });
  const gone = await listen(() => {});
  const liveUrl = `http://127.0.0.1:${live.address().port}/`;
  const goneUrl = `http://127.0.0.1:${gone.address().port}/`;
  await new Promise((resolve) => gone.close(resolve));

  const browser = await browserType.launch(options);
  const row = { name, liveUrl, goneUrl };
  try {
    const page = await browser.newPage();
    await page.goto(liveUrl);
    const navigated = page.waitForEvent('framenavigated', { timeout: TIMEOUT }).then(() => 'committed', (error) => String(error.message).split('\n')[0]);
    await page.evaluate((url) => { window.location.href = url; }, goneUrl);
    row.navigation = await navigated;
    row.urlAfter = page.url();
    const started = Date.now();
    try {
      await page.waitForLoadState('load', { timeout: TIMEOUT });
      row.load = 'resolved';
    } catch (error) {
      row.load = String(error.message).split('\n')[0];
    }
    row.loadMs = Date.now() - started;
    row.documentURI = await page.evaluate(() => document.documentURI).catch((error) => `unread: ${String(error.message).split('\n')[0]}`);
  } finally {
    await browser.close();
    await new Promise((resolve) => live.close(resolve));
  }
  log.write('variant', row);
  return row;
}

const rows = [];
rows.push(await variant('firefox: the page navigates itself to a refused address, then wait for load', firefox, { headless: true }));
rows.push(await variant('chromium: the page navigates itself to a refused address, then wait for load', chromium, { headless: true, channel: 'chromium' }));

await writeFile(join(args.out, 'result.json'), `${JSON.stringify({ finished: iso(), rows }, null, 1)}\n`);
await log.close();
process.exit(0);
