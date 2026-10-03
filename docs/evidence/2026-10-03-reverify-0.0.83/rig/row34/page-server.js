// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr
// A real origin for the durability probe: http://127.0.0.1:<port> is a
// potentially-trustworthy origin, so service workers register there.
'use strict';
const http = require('node:http');

const page = `<!doctype html><meta charset="utf-8"><title>reverify</title><h1>reverify</h1>`;
const sw = `self.addEventListener('install', e => self.skipWaiting());`;

const server = http.createServer((req, res) => {
  if (req.url === '/sw.js') {
    res.writeHead(200, { 'content-type': 'text/javascript', 'cache-control': 'no-store' });
    res.end(sw);
    return;
  }
  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
  res.end(page);
});

server.listen(Number(process.argv[2] || 0), '127.0.0.1', () => {
  console.log(JSON.stringify({ port: server.address().port }));
});
