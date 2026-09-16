// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Renders every SVG in ./svg to ./png at 256/48/32/16 using playwright-core + a
// Chromium already present in the ms-playwright cache. Writes render.done when finished.
import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// Corrected 2026-09-16 (previously HERE = 'C:/Source/SixFive7/BrowserAI/.work/2026-09-15-icons'
// and PW = 'file:///C:/Source/SixFive7/BrowserAI/.work/probe2/node_modules/playwright-core/index.mjs').
// Both pointed into the scratch directory, which was wiped that day, so the
// script could no longer find its own inputs. HERE is now wherever this file
// sits, and playwright-core is taken from the payload the product itself
// ships -- better provenance than the scratch copy, and the only one that is
// still here. Build the payload first (build/Build-Payload.ps1) or set
// BROWSERAI_PLAYWRIGHT_CORE to another index.mjs.
const HERE = dirname(fileURLToPath(import.meta.url));
const PW = process.env.BROWSERAI_PLAYWRIGHT_CORE
  ? pathToFileURL(process.env.BROWSERAI_PLAYWRIGHT_CORE).href
  : pathToFileURL(join(HERE, '..', '..', '..', 'payload', 'mcp', 'node_modules', 'playwright-core', 'index.mjs')).href;
const CHROME = 'C:/Users/jori/AppData/Local/ms-playwright/chromium-1243/chrome-win64/chrome.exe';
const SIZES = [256, 48, 32, 16];

const mod = await import(PW);
const chromium = mod.chromium ?? mod.default.chromium;

const browser = await chromium.launch({
  executablePath: CHROME,
  headless: true,
  args: ['--disable-gpu', '--force-color-profile=srgb', '--disable-lcd-text'],
});
const ctx = await browser.newContext({ deviceScaleFactor: 1 });
const page = await ctx.newPage();

const only = process.argv[2];   // optional stem filter, e.g. candidate-07
const files = readdirSync(join(HERE, 'svg'))
  .filter(f => f.endsWith('.svg') && (!only || f.startsWith(only)))
  .sort();
console.log('svgs: ' + files.length);
for (const f of files) {
  const svg = readFileSync(join(HERE, 'svg', f), 'utf8');
  const stem = f.replace(/\.svg$/, '');
  for (const size of SIZES) {
    const html = `<!doctype html><meta charset="utf-8"><style>
      html,body{margin:0;padding:0;background:transparent;overflow:hidden}
      svg{display:block;width:${size}px;height:${size}px}</style>${svg}`;
    await page.setViewportSize({ width: size, height: size });
    await page.setContent(html, { waitUntil: 'load' });
    await page.screenshot({
      path: join(HERE, 'png', `${stem}-${String(size).padStart(3, '0')}.png`),
      omitBackground: true,
      clip: { x: 0, y: 0, width: size, height: size },
    });
  }
  console.log('rendered ' + stem);
}
await browser.close();
writeFileSync(join(HERE, 'render.done'), 'ok\n');
console.log('DONE');
