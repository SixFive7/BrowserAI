// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Re-runs the exact page in the #23047 comment draft, so the quoted output is
// the output of that page. Usage: node shadow-exact.cjs <playwright-core dir> <outdir>
'use strict';
const fs = require('fs');
const path = require('path');
const [PW, OUT] = process.argv.slice(2);
fs.mkdirSync(OUT, { recursive: true });
const { chromium, firefox } = require(PW);
const HTML = `<h1>Shadow roots</h1>
<open-box></open-box>
<closed-box></closed-box>
<script>
customElements.define('open-box', class extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({ mode: 'open' }).innerHTML = '<p>Text in an open root</p><button>Open root button</button>';
  }
});
customElements.define('closed-box', class extends HTMLElement {
  constructor() {
    super();
    this.attachShadow({ mode: 'closed' }).innerHTML = '<p>Text in a closed root</p><button>Closed root button</button>';
  }
});
</script>`;
(async () => {
  const out = { playwrightCore: require(path.join(PW, 'package.json')).version };
  for (const [name, type] of [['chromium', chromium], ['firefox', firefox]]) {
    const browser = await type.launch({ headless: true });
    const page = await browser.newPage();
    await page.setContent(HTML);
    const r = { version: browser.version(), ariaSnapshot: await page.locator('body').ariaSnapshot(), buttons: await page.getByRole('button').count() };
    if (name === 'chromium') {
      const cdp = await page.context().newCDPSession(page);
      const { nodes } = await cdp.send('Accessibility.getFullAXTree');
      r.axNamed = nodes.filter((n) => !n.ignored && n.name && n.name.value).map((n) => `${n.role.value}: ${n.name.value}`).filter((s) => /root/.test(s) && !/InlineTextBox/.test(s));
    }
    out[name] = r;
    await browser.close();
  }
  fs.writeFileSync(path.join(OUT, 'shadow-exact.json'), JSON.stringify(out, null, 2));
  console.log(JSON.stringify(out, null, 2));
})().catch((e) => { console.error(e); process.exit(1); });
