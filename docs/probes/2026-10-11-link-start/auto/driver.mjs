// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Drives stock Firefox 157 over WebDriver BiDi for the 55 a measurement. It clicks the
// probe's in-page links; BiDi input is a trusted click inside the page and moves nothing
// of the real mouse or keyboard. Before each click it foregrounds the Firefox window
// through focus.ps1 (no synthetic input), because the chain under test begins with
// Firefox in front. After each it closes the window the link opened through
// close-probe-windows.ps1. Every step is written to steps.json for the observer's record
// to be read beside.

import { spawnSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { setTimeout as sleep } from 'node:timers/promises';
import puppeteer from 'puppeteer-core';

const FIREFOX = 'C:\\Program Files\\Mozilla Firefox\\firefox.exe';
const HERE = 'C:\\Source\\SixFive7\\BrowserAI\\.work\\link-55a';
const PROFILE = HERE + '\\ff-profile';
const P1 = 'http://127.0.0.1:53117/t1/';
const P1B = 'http://127.0.0.1:53117/t2/';

const prefs = {
  'network.protocol-handler.external.browserai-probe': true,
  'network.protocol-handler.prompt-without-user-activation': false,
  'network.protocol-handler.warn-external.browserai-probe': false,
  'browser.shell.checkDefaultBrowser': false,
  'browser.aboutwelcome.enabled': false,
};

function ps(script, args) {
  const result = spawnSync('pwsh', ['-NoProfile', '-NonInteractive', '-File', HERE + '\\' + script, ...args], { encoding: 'utf8', timeout: 30000 });
  const out = (result.stdout || '').trim();
  try { return JSON.parse(out); } catch { return { raw: out, err: (result.stderr || '').trim() }; }
}

const steps = [];

const browser = await puppeteer.launch({
  browser: 'firefox',
  executablePath: FIREFOX,
  headless: false,
  userDataDir: PROFILE,
  protocol: 'webDriverBiDi',
  extraPrefsFirefox: prefs,
  args: ['--width=1200', '--height=900'],
  timeout: 60000,
});

const ffPid = browser.process()?.pid ?? 0;
writeFileSync(HERE + '\\ff-pid.txt', String(ffPid));
console.log('firefox pid', ffPid);

const page = (await browser.pages())[0] ?? await browser.newPage();

let currentUrl = '';
async function run(label, url, linkId, at) {
  if (url !== currentUrl) {
    await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 25000 });
    currentUrl = url;
  }
  await page.waitForSelector('#' + linkId, { timeout: 10000 });
  await sleep(400);
  const focus = ps('focus.ps1', []);
  await sleep(400);
  const clickedAt = new Date().toISOString();
  await page.evaluate((id) => document.getElementById(id).click(), linkId);
  await sleep(3800);
  const closed = ps('close-probe-windows.ps1', []);
  await sleep(1200);
  const step = { label, url, link: linkId, at, clickedAtUtc: clickedAt, focus, closed };
  steps.push(step);
  console.log(label, 'focusFF=' + (focus.isFirefoxForeground ?? '?'), 'closed=' + (Array.isArray(closed) ? closed.length : JSON.stringify(closed)));
}

try {
  // The order the steps give: A, C, E, B, D on page 1, then A on page 1b.
  await run('1 (A, open-itself)', P1, 'A', 'p1');
  await run('3 (C, pick-itself)', P1, 'C', 'p1');
  await run('4 (E, stand-in no grant, CONTROL)', P1, 'E', 'p1');
  await run('5 (B, stand-in granted, open)', P1, 'B', 'p1');
  await run('6 (D, stand-in granted, pick)', P1, 'D', 'p1');
  await run('7 (A on page 1b, same port new path)', P1B, 'A', 'p1b');
} catch (failure) {
  console.log('driver error:', failure?.message ?? failure);
  steps.push({ error: String(failure?.message ?? failure) });
} finally {
  writeFileSync(HERE + '\\run-20261011T161900Z\\steps.json', JSON.stringify(steps, null, 2));
  console.log('steps written');
  try { await browser.close(); console.log('firefox closed'); } catch (closeFailure) { console.log('close error:', closeFailure?.message ?? closeFailure); }
}
