// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Scratch probe: what Playwright's ARIA snapshot shows of content inside a
// closed shadow root, next to the browser's own accessibility tree.
// Usage: node shadow-probe.cjs <playwright-core dir> <outdir> [<@playwright/mcp dir>]
'use strict';
const fs = require('fs');
const path = require('path');
const http = require('http');
const { spawn } = require('child_process');

const [PW, OUT, MCPDIR] = process.argv.slice(2);
const SCRATCH = path.resolve(__dirname, '..');
const under = (p) => !!p && path.resolve(p).toLowerCase().startsWith(SCRATCH.toLowerCase() + path.sep);
for (const k of ['LOCALAPPDATA', 'TEMP', 'TMP', 'PLAYWRIGHT_BROWSERS_PATH']) {
  if (!under(process.env[k])) { console.error(`REFUSING: ${k} is not under ${SCRATCH}`); process.exit(2); }
}
fs.mkdirSync(OUT, { recursive: true });
const LOG = path.join(OUT, 'shadow-probe.log');
fs.writeFileSync(LOG, '');
const log = (...a) => fs.appendFileSync(LOG, a.map((x) => (typeof x === 'string' ? x : JSON.stringify(x))).join(' ') + '\n');
const { chromium, firefox } = require(PW);

const PAGE = `<!doctype html>
<html><head><meta charset="utf-8"><title>closed shadow root</title></head>
<body>
<h1>Shadow roots</h1>
<open-box></open-box>
<closed-box></closed-box>
<script>
customElements.define('open-box', class extends HTMLElement {
  constructor() {
    super();
    const root = this.attachShadow({ mode: 'open' });
    root.innerHTML = '<p>Text in an open root</p><button>Open root button</button>';
  }
});
customElements.define('closed-box', class extends HTMLElement {
  constructor() {
    super();
    const root = this.attachShadow({ mode: 'closed' });
    root.innerHTML = '<p>Text in a closed root</p><button>Closed root button</button>';
    root.querySelector('button').addEventListener('click', () => { document.title = 'closed root button clicked'; });
  }
});
</script>
</body></html>
`;
fs.writeFileSync(path.join(OUT, 'closed-shadow.html'), PAGE);

(async () => {
  const server = http.createServer((req, res) => { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); res.end(PAGE); });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  const url = `http://127.0.0.1:${server.address().port}/`;
  const result = { playwrightCore: require(path.join(PW, 'package.json')).version, browsers: {} };
  log('playwright-core', result.playwrightCore, 'node', process.version);
  for (const [name, type] of [['chromium', chromium], ['firefox', firefox]]) {
    const r = {};
    const browser = await type.launch({ headless: true, timeout: 60000 });
    r.version = browser.version();
    const page = await browser.newPage();
    await page.goto(url);
    r.ariaSnapshot = await page.locator('body').ariaSnapshot();
    r.buttonsByRole = await page.getByRole('button').count();
    r.closedButtonByRole = await page.getByRole('button', { name: 'Closed root button' }).count();
    r.closedTextByText = await page.getByText('Text in a closed root').count();
    r.openButtonByRole = await page.getByRole('button', { name: 'Open root button' }).count();
    // The closed box's own layout box, from the light DOM element that hosts it.
    const host = await page.locator('closed-box').boundingBox();
    r.hostBox = host;
    if (host) {
      // The button is the last thing in the host's box; click near its bottom-left corner.
      await page.mouse.click(host.x + 10, host.y + host.height - 5);
      r.titleAfterMouseClick = await page.title();
    }
    if (name === 'chromium') {
      const cdp = await page.context().newCDPSession(page);
      const { nodes } = await cdp.send('Accessibility.getFullAXTree');
      const named = nodes.filter((n) => !n.ignored).map((n) => `${n.role && n.role.value}: ${n.name && n.name.value}`);
      r.cdpAxTreeHasClosedButton = named.some((s) => /Closed root button/.test(s));
      r.cdpAxTreeHasClosedText = named.some((s) => /Text in a closed root/.test(s));
      r.cdpAxTreeExcerpt = named.filter((s) => /root|Shadow/.test(s));
    }
    await browser.close();
    result.browsers[name] = r;
    log(`=== ${name} ${r.version}`);
    log(r.ariaSnapshot);
    log(JSON.stringify({ ...r, ariaSnapshot: undefined }, null, 1));
  }

  if (MCPDIR) {
    // The same page through @playwright/mcp's browser_snapshot.
    const proc = spawn(process.execPath, [path.join(MCPDIR, 'cli.js'), '--headless', '--browser=chromium', '--isolated', `--output-dir=${path.join(OUT, 'mcp-out')}`], { cwd: OUT, env: { ...process.env }, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    let buf = ''; const pending = new Map(); let id = 1;
    proc.stdout.on('data', (d) => { buf += d; let i; while ((i = buf.indexOf('\n')) >= 0) { const line = buf.slice(0, i); buf = buf.slice(i + 1); try { const m = JSON.parse(line); if (pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); } } catch {} } });
    const send = (method, params) => new Promise((r) => { const n = id++; pending.set(n, r); proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: n, method, params }) + '\n'); });
    const init = await send('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'shadow-probe', version: '0' } });
    proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
    await send('tools/call', { name: 'browser_navigate', arguments: { url } });
    const snap = await send('tools/call', { name: 'browser_snapshot', arguments: {} });
    const text = snap.result.content.filter((c) => c.type === 'text').map((c) => c.text).join('\n');
    result.mcp = { version: require(path.join(MCPDIR, 'package.json')).version, server: init.result.serverInfo, snapshot: text, hasClosedButton: /Closed root button/.test(text), hasOpenButton: /Open root button/.test(text) };
    log('=== @playwright/mcp', result.mcp.version, JSON.stringify(init.result.serverInfo));
    log(text);
    proc.stdin.end();
    await new Promise((r) => { proc.on('exit', r); setTimeout(r, 8000); });
  }
  fs.writeFileSync(path.join(OUT, 'shadow-probe-results.json'), JSON.stringify(result, null, 2));
  server.close();
  setTimeout(() => process.exit(0), 300);
})().catch((e) => { log('FATAL', String(e && e.stack || e)); process.exit(1); });
