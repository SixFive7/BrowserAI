// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// repro.mjs
import { firefox } from 'playwright-core';
import { createServer } from 'node:http';

const listen = (handler) => new Promise((resolve) => {
  const server = createServer(handler);
  server.listen(0, '127.0.0.1', () => resolve(server));
});

const live = await listen((request, response) => {
  response.writeHead(200, { 'content-type': 'text/html' });
  response.end('<title>live</title><p>live</p>');
});
const gone = await listen(() => {});
const goneUrl = `http://127.0.0.1:${gone.address().port}/`;
await new Promise((resolve) => gone.close(resolve)); // nothing listens there any more

const browser = await firefox.launch({ headless: true });
const page = await browser.newPage();
await page.goto(`http://127.0.0.1:${live.address().port}/`);
const navigated = page.waitForEvent('framenavigated');
await page.evaluate((url) => { window.location.href = url; }, goneUrl);
await navigated;
const start = Date.now();
try {
  await page.waitForLoadState('load', { timeout: 15000 });
  console.log(`load after ${Date.now() - start} ms`);
} catch (e) {
  console.log(e.message.split('\n')[0]);
}
console.log(page.url(), await page.evaluate(() => document.documentURI));
await browser.close();
live.close();
