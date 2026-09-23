// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

'use strict';
// Step 1 of the upstream ask: re-verify the zero-byte webp on the RAW upstream
// child, with no BrowserAI process on the path at all. node.exe + cli.js only.
//   payload/node/node.exe payload/mcp/node_modules/@playwright/mcp/cli.js
// A local tall page over 127.0.0.1 (file: is refused by upstream's workspace
// root check), webp fullPage at 16383 and 16384, png control at 16384.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { makeClient, BROWSER_HANG_MS } = require('./rpc.js');

const REPO = 'c:\\Source\\SixFive7\\BrowserAI';
const TAG = process.argv[2] || 'default';
const OUT = path.join(REPO, '.work', '2026-09-14-webp-ask', 'raw-' + TAG);
const NODE = path.join(REPO, 'payload', 'node', 'node.exe');
const CLI = path.join(REPO, 'payload', 'mcp', 'node_modules', '@playwright', 'mcp', 'cli.js');
const BROWSERS = 'C:\\Users\\jori\\AppData\\Local\\BrowserAI\\browsers';

fs.rmSync(OUT, { recursive: true, force: true });
fs.mkdirSync(path.join(OUT, 'output'), { recursive: true });
const log = (...a) => { const s = [new Date().toISOString(), ...a].join(' '); console.log(s); fs.appendFileSync(path.join(OUT, 'raw.log'), s + '\n'); };

// Same decoder as the m2d probe: width/height straight out of the container.
function dims(buf) {
  if (buf.length > 26 && buf[0] === 0x89 && buf.toString('latin1', 1, 4) === 'PNG') return { fmt: 'png', w: buf.readUInt32BE(16), h: buf.readUInt32BE(20) };
  if (buf.length > 4 && buf[0] === 0xFF && buf[1] === 0xD8) {
    let i = 2;
    while (i < buf.length - 9) {
      if (buf[i] !== 0xFF) { i++; continue; }
      const m = buf[i + 1];
      if (m >= 0xC0 && m <= 0xCF && m !== 0xC4 && m !== 0xC8 && m !== 0xCC) return { fmt: 'jpeg', h: buf.readUInt16BE(i + 5), w: buf.readUInt16BE(i + 7) };
      i += 2 + buf.readUInt16BE(i + 2);
    }
    return { fmt: 'jpeg', w: null, h: null };
  }
  if (buf.length > 30 && buf.toString('latin1', 0, 4) === 'RIFF' && buf.toString('latin1', 8, 12) === 'WEBP') {
    const cc = buf.toString('latin1', 12, 16);
    if (cc === 'VP8X') return { fmt: 'webp', w: 1 + buf.readUIntLE(24, 3), h: 1 + buf.readUIntLE(27, 3), sub: cc };
    if (cc === 'VP8L') { const b = buf.readUInt32LE(21); return { fmt: 'webp', w: (b & 0x3FFF) + 1, h: ((b >> 14) & 0x3FFF) + 1, sub: cc }; }
    if (cc === 'VP8 ') return { fmt: 'webp', w: buf.readUInt16LE(26) & 0x3FFF, h: buf.readUInt16LE(28) & 0x3FFF, sub: cc };
    return { fmt: 'webp', w: null, h: null, sub: cc };
  }
  return { fmt: buf.length === 0 ? 'EMPTY' : 'unknown', w: null, h: null };
}

// height -> [types]
const PLAN = [
  [16383, ['webp']],
  [16384, ['webp', 'png', 'jpeg']],
];
const rows = [];
const facts = {};

(async () => {
  const server = http.createServer((req, res) => {
    const m = req.url.match(/^\/h\/(\d+)/);
    const h = m ? Number(m[1]) : 1000;
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    res.end('<!doctype html><html><head><meta charset="utf-8"><title>h' + h + '</title>' +
      '<style>html,body{margin:0;padding:0}#p{height:' + h + 'px;background:repeating-linear-gradient(0deg,#123 0 20px,#9bd 20px 40px)}</style>' +
      '</head><body><div id="p"></div></body></html>');
  });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const base = 'http://127.0.0.1:' + server.address().port;
  log('page server', base);

  // Upstream's own defaults, with two exceptions that are both about not
  // damaging this machine: SKIP_BROWSER_GC stops upstream's stale-browser
  // collector deleting the provisioned tree, SKIP_BROWSER_DOWNLOAD makes a
  // missing browser a loud failure and not a 767 MiB download.
  const env = Object.assign({}, process.env, {
    PLAYWRIGHT_BROWSERS_PATH: BROWSERS,
    PLAYWRIGHT_SKIP_BROWSER_GC: '1',
    PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD: '1',
  });
  const args = [CLI, '--headless', '--isolated', '--output-dir', path.join(OUT, 'output'), '--viewport-size', '1280x720'];
if (TAG !== 'default') { args.push('--browser', TAG); }
  log('spawn', NODE, JSON.stringify(args));
  const c = makeClient(NODE, args, { cwd: OUT, env, stderrLog: path.join(OUT, 'child.stderr.log') });
  facts.childPid = c.child.pid;
  log('raw @playwright/mcp child pid:', c.child.pid);

  const init = await c.rpc('initialize', { protocolVersion: '2025-11-25', capabilities: {}, clientInfo: { name: 'webp-ask-raw', version: '1' } }, 120000);
  facts.serverInfo = init.msg.result && init.msg.result.serverInfo;
  log('initialize ->', JSON.stringify(facts.serverInfo));
  c.notify('notifications/initialized');

  const tl = await c.rpc('tools/list', undefined, 120000);
  const tools = (tl.msg.result && tl.msg.result.tools) || [];
  const shot = tools.find(t => t.name === 'browser_take_screenshot');
  facts.toolCount = tools.length;
  facts.screenshotSchemaKeys = shot ? Object.keys(shot.inputSchema.properties || {}) : null;
  facts.screenshotTypeEnum = shot && shot.inputSchema.properties && shot.inputSchema.properties.type ? shot.inputSchema.properties.type : null;
  log('tools/list -> ' + tools.length + ' tools; browser_take_screenshot props: ' + JSON.stringify(facts.screenshotSchemaKeys));

  const call = (n, a) => c.rpc('tools/call', { name: n, arguments: a || {} }, BROWSER_HANG_MS);
  const textOf = r => ((r.msg.result && r.msg.result.content) || []).filter(x => x.type === 'text').map(x => x.text).join('\n');

  // Chromium's own version, read from the browser this run is actually driving.
  await call('browser_navigate', { url: base + '/h/100' });
  const uaR = await call('browser_evaluate', { function: '() => navigator.userAgent' });
  facts.userAgent = textOf(uaR);
  log('userAgent ->', facts.userAgent.replace(/\s+/g, ' ').slice(0, 400));

  const outDir = path.join(OUT, 'output');
  for (const [h, types] of PLAN) {
    await call('browser_navigate', { url: base + '/h/' + h });
    const mr = await call('browser_evaluate', { function: '() => String(document.documentElement.scrollHeight)' });
    const docH = (textOf(mr).match(/(\d{3,})/) || [])[1];
    for (const type of types) {
      const before = new Set(fs.readdirSync(outDir));
      const r = await call('browser_take_screenshot', { fullPage: true, type });
      const content = (r.msg.result && r.msg.result.content) || [];
      const img = content.find(x => x.type === 'image');
      const fresh = fs.readdirSync(outDir).filter(f => !before.has(f) && /\.(png|jpe?g|webp)$/i.test(f));
      const diskBuf = fresh.length ? fs.readFileSync(path.join(outDir, fresh[0])) : null;
      const inlineBuf = img ? Buffer.from(img.data, 'base64') : null;
      const row = {
        askedHeight: h, docHeight: docH ? Number(docH) : null, type, ms: r.ms,
        isError: !!(r.msg.result && r.msg.result.isError),
        rpcError: r.msg.error ? JSON.stringify(r.msg.error).slice(0, 400) : null,
        inlineMime: img ? img.mimeType : null,
        inlineBytes: inlineBuf ? inlineBuf.length : null,
        inlineDims: inlineBuf ? dims(inlineBuf) : null,
        diskFile: fresh.length ? fresh[0] : null,
        diskBytes: diskBuf ? diskBuf.length : null,
        diskDims: diskBuf ? dims(diskBuf) : null,
        text: textOf(r).slice(0, 400),
      };
      rows.push(row);
      log('h=' + h + ' doc=' + docH + ' ' + type.padEnd(4) + ' -> ' + String(r.ms).padStart(5) + ' ms err=' + row.isError +
        ' mime=' + row.inlineMime +
        ' inline=' + row.inlineBytes + 'B ' + (row.inlineDims ? row.inlineDims.w + 'x' + row.inlineDims.h : '-') +
        ' disk=' + row.diskBytes + 'B ' + (row.diskDims ? row.diskDims.w + 'x' + row.diskDims.h : '-'));
      fs.writeFileSync(path.join(OUT, 'raw-rows.json'), JSON.stringify({ facts, rows }, null, 2));
    }
  }

  await call('browser_close', {});
  c.child.stdin.end();
  await new Promise(res => { c.child.on('exit', code => { log('raw child exited code=' + code); facts.exitCode = code; res(); }); setTimeout(() => { log('raw child did NOT exit within 30 s'); facts.exitCode = 'timeout'; res(); }, 30000); });
  server.close();
  fs.writeFileSync(path.join(OUT, 'raw-rows.json'), JSON.stringify({ facts, rows }, null, 2));
  log('=== done ===');
  process.exit(0);
})().catch(e => {
  log('FAILED:', (e && e.stack) || String(e));
  fs.writeFileSync(path.join(OUT, 'raw-rows.json'), JSON.stringify({ facts, rows }, null, 2));
  process.exit(1);
});
