// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Q380: a full-page screenshot of a page about 50,000 px tall, taken through
// the published BrowserAI.Server.exe, headless, so the image can be checked for
// content that repeats every 16,384 px. The page is the local site's /tall:
// 100 px bands whose colour encodes their own index.
//
//   node tall.mjs server=<exe> browser=chromium|firefox height=50000 run=<tag> out=<dir> sessions=<dir> [viewport=WxH]

import { mkdirSync, writeFileSync, rmSync, copyFileSync, existsSync, statSync } from 'node:fs';
import path from 'node:path';
import { startSite } from './site.mjs';
import { Client, evaluated } from './mcp-client.mjs';

const opt = Object.fromEntries(process.argv.slice(2).map((a) => { const i = a.indexOf('='); return [a.slice(0, i), a.slice(i + 1)]; }));
const out = opt.out;
rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });
const sessionDir = path.join(opt.sessions, `tall-${opt.browser}-${opt.run}`);
rmSync(sessionDir, { recursive: true, force: true });
const height = Number(opt.height ?? '50000');
const result = { browser: opt.browser, height, viewport: opt.viewport ?? 'default', session: sessionDir, startedUtc: new Date().toISOString(), steps: [] };
const save = () => writeFileSync(path.join(out, 'result.json'), JSON.stringify(result, null, 2));

const site = await startSite(opt.run);
const client = new Client(opt.server, { cwd: out, stderrPath: path.join(out, 'server.stderr.log'), logPath: path.join(out, 'calls.log') });

const step = async (label, tool, args, timeoutMs) => {
  const withSession = tool.startsWith('browserai_') ? args : { session: sessionDir, why: `Q380 tall screenshot rig: ${label}`, ...args };
  let answer;
  try { answer = await client.call(tool, withSession, timeoutMs); } catch (e) { answer = { ok: false, isError: true, text: `THREW ${e.message}`, ms: null, images: 0 }; }
  result.steps.push({ label, tool, ok: answer.ok, isError: answer.isError, ms: answer.ms, images: answer.images, text: answer.text.slice(0, 3000) });
  save();
  return answer;
};

try {
  result.handshake = await client.handshake();
  await step('init', 'browserai_init', {
    directory: sessionDir,
    purpose: `Q380 rig: a ${height} px local page screenshotted full-page on ${opt.browser}; destroyed at the end of the run.`,
    browser: opt.browser,
    headed: false,
    ...(opt.viewport ? { viewport: opt.viewport } : {}),
  });
  await step('navigate tall', 'browser_navigate', { url: `${site.origin}/tall?h=${height}` });
  result.page = evaluated((await step('measure the page', 'browser_evaluate', {
    function: "async () => { for (let i = 0; i < 100 && !document.title.startsWith('tall ready'); i++) await new Promise((r) => setTimeout(r, 100)); return JSON.stringify({ title: document.title, scrollHeight: document.documentElement.scrollHeight, scrollWidth: document.documentElement.scrollWidth, inner: [innerWidth, innerHeight], dpr: devicePixelRatio, ua: navigator.userAgent }); }",
  })).text);
  const file = `tall-${opt.browser}-${opt.run}.png`;
  const shot = await step('full-page screenshot', 'browser_take_screenshot', { fullPage: true, filename: file, type: 'png', scale: 'css' }, 600000);
  const produced = path.join(sessionDir, 'output', file);
  result.screenshot = { file: produced, exists: existsSync(produced), bytes: existsSync(produced) ? statSync(produced).size : 0, answerMs: shot.ms, isError: shot.isError };
  if (existsSync(produced)) copyFileSync(produced, path.join(out, file));
} catch (e) {
  result.fatal = String(e?.stack ?? e);
} finally {
  await step('destroy', 'browserai_destroy', { directory: sessionDir, why: 'Q380 rig: the screenshot is copied out; the session is done' });
  result.serverExit = await client.close();
  await site.close();
  result.endedUtc = new Date().toISOString();
  save();
  setTimeout(() => process.exit(0), 500);
}
