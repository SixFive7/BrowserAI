// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Renders the shipped icon assets and the repository's social preview from the
// master SVG, with the same headless Chromium + playwright-core pipeline the ten
// candidates were drawn with on 2026-09-15
// (docs/design/icon-candidates/render.mjs).
// Nothing is downloaded; the browser is the one already in the machine's cache.
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// Corrected 2026-09-16: HERE, MASTER and PW all pointed into the scratch
// directory, which was wiped that day (previously
// 'C:/Source/SixFive7/BrowserAI/.work/2026-09-16-icon',
// '.../.work/2026-09-15-icons/svg/candidate-03.svg' and
// 'file:///.../.work/probe2/node_modules/playwright-core/index.mjs'). MASTER is
// now assets/icon.svg, which IS candidate 3 and is the file the product ships;
// playwright-core comes from the payload; output goes to the scratch root,
// which is where a generated asset belongs until somebody copies it into
// assets/ deliberately. Build the payload first (build/Build-Payload.ps1) or
// set BROWSERAI_PLAYWRIGHT_CORE to another index.mjs.
const REPO = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const HERE = join(REPO, '.work', '2026-09-16-icon');
const MASTER = join(REPO, 'assets', 'icon.svg');
const PW = process.env.BROWSERAI_PLAYWRIGHT_CORE
  ? pathToFileURL(process.env.BROWSERAI_PLAYWRIGHT_CORE).href
  : pathToFileURL(join(REPO, 'payload', 'mcp', 'node_modules', 'playwright-core', 'index.mjs')).href;
const CHROME = 'C:/Users/jori/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe';

const svg = readFileSync(MASTER, 'utf8');
const out = join(HERE, 'out');
mkdirSync(out, { recursive: true });

const mod = await import(PW);
const chromium = mod.chromium ?? mod.default.chromium;

const browser = await chromium.launch({
  executablePath: CHROME,
  headless: true,
  args: ['--disable-gpu', '--force-color-profile=srgb', '--disable-lcd-text'],
});
const ctx = await browser.newContext({ deviceScaleFactor: 1 });
const page = await ctx.newPage();

// --- the icon, at the sizes the repository ships -----------------------------
for (const size of [256, 128]) {
  const html = `<!doctype html><meta charset="utf-8"><style>
    html,body{margin:0;padding:0;background:transparent;overflow:hidden}
    svg{display:block;width:${size}px;height:${size}px}</style>${svg}`;
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(html, { waitUntil: 'load' });
  await page.screenshot({
    path: join(out, `icon-${size}.png`),
    omitBackground: true,
    clip: { x: 0, y: 0, width: size, height: size },
  });
  console.log(`icon-${size}.png`);
}

// --- the social preview, 1280x640, GitHub's own recommended size -------------
// The palette is the icon's: #083344 is its pupil, #ECFEFF its wireframe,
// #F0FDFA its sclera and #34D399 -> #0E7490 its sphere.
const card = `<!doctype html><meta charset="utf-8"><style>
  html,body{margin:0;padding:0;width:1280px;height:640px;overflow:hidden}
  body{background:#083344;font-family:'Segoe UI',system-ui,sans-serif;
       -webkit-font-smoothing:antialiased}
  .sheet{display:flex;align-items:center;height:640px;padding:0 112px;box-sizing:border-box;gap:72px}
  .mark{flex:0 0 288px;height:288px}
  .mark svg{display:block;width:288px;height:288px}
  h1{margin:0;font-size:104px;line-height:1;font-weight:600;color:#F0FDFA;letter-spacing:-1px}
  p{margin:28px 0 0;font-size:40px;line-height:1.25;font-weight:400;color:#5EEAD4}
  .rule{position:absolute;left:0;right:0;bottom:0;height:12px;
        background:linear-gradient(90deg,#34D399 0%,#10B981 55%,#0E7490 100%)}
</style>
<div class="sheet">
  <div class="mark">${svg}</div>
  <div>
    <h1>BrowserAI</h1>
    <p>A real browser for your AI agent.</p>
  </div>
</div>
<div class="rule"></div>`;

await page.setViewportSize({ width: 1280, height: 640 });
await page.setContent(card, { waitUntil: 'load' });
await page.screenshot({
  path: join(out, 'social-preview.png'),
  clip: { x: 0, y: 0, width: 1280, height: 640 },
});
console.log('social-preview.png');

await browser.close();
writeFileSync(join(HERE, 'render-assets.done'), 'ok\n');
console.log('DONE');
