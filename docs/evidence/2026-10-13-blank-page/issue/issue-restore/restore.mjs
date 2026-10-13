// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// restore.mjs
import { firefox } from 'playwright-core';
import fs from 'node:fs';
import http from 'node:http';
import os from 'node:os';
import path from 'node:path';

// no-store, so Firefox cannot bring the page back from its cache once the server is gone
const server = http.createServer((req, res) => {
  res.writeHead(200, { 'content-type': 'text/html', 'cache-control': 'no-store' });
  res.end('<title>restored</title><p>restored</p>');
});
await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const url = `http://127.0.0.1:${server.address().port}/`;

const userDataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'ff-restore-'));
const options = {
  headless: true,
  firefoxUserPrefs: { 'browser.sessionstore.resume_session_once': true },
  ignoreDefaultArgs: ['about:blank'],
};

// First launch: open the page and close cleanly, so Firefox saves the session.
let context = await firefox.launchPersistentContext(userDataDir, options);
const page = context.pages()[0] ?? await context.newPage();
await page.goto(url);
await context.close();

await new Promise((resolve) => server.close(resolve));

// Second launch: Firefox restores the tab, the load fails and the tab shows the error page.
const start = Date.now();
try {
  context = await firefox.launchPersistentContext(userDataDir, { ...options, timeout: 30000 });
  console.log(`launched in ${Date.now() - start} ms`);
  await context.close();
} catch (e) {
  console.log(`${e.message.split('\n')[0]} (after ${Date.now() - start} ms)`);
}
