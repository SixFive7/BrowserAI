// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// One repetition of the blank-page measurement: a Firefox profile given its tabs through the
// payload's own @playwright/mcp child, closed cleanly, and resumed twice by a new child each
// time, with Playwright's blank page kept, which is the config BrowserAI writes for a Firefox
// session since the maintainer's 161. Each resume lists the tabs while Firefox's session
// restore reopens them, reads which tab Firefox selected, makes the first reopened tab current
// and closes the blank page the way FirefoxRestore does, checks that the context still answers,
// and closes again, so the second resume shows what the first one left.

import { mkdir, readFile, stat, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import {
  EventLog, McpClient, childEnvironment, delay, iso, parseArgs, probePort, readFirefoxSession, required, startPageServer, waitForFile,
} from '../2026-10-13-restore-stall/lib.mjs';

// ChildConnection.ChildProtocolVersion, as the restore-stall rig sends it.
const PROTOCOL = '2025-11-25';
const CALL_BOUND_MS = 240000;
const POLL_MS = 250;
const POLL_FOR_MS = 12000;
const RECOVERY_WAIT_MS = 25000;
const DEBUG = 'pw:protocol,pw:browser,pw:api';
const BLANK = 'about:blank';

// What each arm opens before the first close: how many pages, and whether each page's server
// ends before the resumes. 'none' opens no page, so the session Firefox saves holds the blank
// page alone; 'fresh' does not run the first child at all, so the profile holds no session.
const ARMS = {
  'one-dead': [{ ends: true }],
  'one-alive': [{ ends: false }],
  'three-mixed': [{ ends: true }, { ends: false }, { ends: false }],
  none: [],
  fresh: null,
};

const args = required(parseArgs(process.argv.slice(2)), 'cli', 'browsers', 'registry', 'template', 'arm', 'round', 'out', 'go');
if (!(args.arm in ARMS)) throw new Error(`unknown arm ${args.arm}`);
await waitForFile(args.go);
await mkdir(args.out, { recursive: true });

const log = new EventLog(join(args.out, 'events.jsonl'));
const profile = join(args.out, 'profile');
for (const folder of ['profile', 'output', 'downloads', 'tmp']) await mkdir(join(args.out, folder), { recursive: true });

// BrowserAI's Firefox config, taken from a resume of 2026-10-13, with this repetition's folders
// and the one change of 161: nothing in ignoreDefaultArgs, so Playwright's blank page is kept.
const config = JSON.parse(await readFile(args.template, 'utf8'));
config.browser.userDataDir = profile;
config.browser.launchOptions.downloadsPath = join(args.out, 'downloads');
config.browser.launchOptions.ignoreDefaultArgs = [];
config.outputDir = join(args.out, 'output');
const configPath = join(args.out, 'config.json');
await writeFile(configPath, `${JSON.stringify(config, null, 2)}\n`);

const env = childEnvironment(process.env, {
  PLAYWRIGHT_BROWSERS_PATH: args.browsers, TEMP: join(args.out, 'tmp'), TMP: join(args.out, 'tmp'), PWTEST_SERVER_REGISTRY: args.registry, DEBUG,
});

const result = { arm: args.arm, round: Number(args.round), started: iso(), config: configPath, pages: [], cycles: [] };
const save = () => writeFile(join(args.out, 'result.json'), `${JSON.stringify(result, null, 1)}\n`);

const child = (label) => new McpClient(process.execPath, [args.cli, '--config', configPath, '--sandbox'], {
  cwd: join(args.out, 'output'), env, stderrPath: join(args.out, `${label}.stderr.txt`), log, label,
});

const tabs = (answer) => (answer?.text ?? '').split('\n')
  .map((line) => /^- (\d+):( \(current\))? \[(.*)\]\((.*)\)( \[crashed\])?$/.exec(line))
  .filter(Boolean)
  .map((match) => ({ index: Number(match[1]), current: !!match[2], title: match[3], url: match[4], crashed: !!match[5] }));

const probe = '() => ({ visibility: document.visibilityState, hidden: document.hidden, documentURI: document.documentURI, title: document.title, href: location.href })';

// FirefoxRestore.ErrorPageRead, copied and not shared: every tab's address and document URI,
// read at once through browser_run_code_unsafe, which is how the resume tells a tab on
// Firefox's error page from the rest.
const ERROR_PAGE_READ = 'async (page) => Promise.all(page.context().pages().map(async (each) => ({ url: each.url(), documentURI: await each.evaluate(() => document.documentURI).catch(() => null) })))';
const readAll = async (client, tabCount) => {
  const answer = await client.call('browser_run_code_unsafe', { code: ERROR_PAGE_READ }, CALL_BOUND_MS);
  const line = answer.text.split('\n').find((candidate) => candidate.trim().startsWith('['));
  let pages = null;
  try { pages = line ? JSON.parse(line.trim()) : null; } catch { pages = null; }
  return { ms: answer.ms, isError: answer.isError, tabs: tabCount, msPerTab: tabCount ? Math.round(answer.ms / tabCount) : null, pages, text: pages ? undefined : answer.text.slice(0, 400) };
};
const evaluate = async (client) => {
  const answer = await client.call('browser_evaluate', { function: probe }, CALL_BOUND_MS);
  const at = answer.text.indexOf('{');
  let value = null;
  try { value = at < 0 ? null : JSON.parse(answer.text.slice(at, answer.text.lastIndexOf('}') + 1)); } catch { value = null; }
  return { ms: answer.ms, isError: answer.isError, value, text: value ? undefined : answer.text.slice(0, 400) };
};

const recoveryAfter = async (since) => {
  const path = join(profile, 'sessionstore-backups', 'recovery.jsonlz4');
  const started = Date.now();
  while (Date.now() - started < RECOVERY_WAIT_MS) {
    try { if ((await stat(path)).mtimeMs > since) return { waitedMs: Date.now() - started, files: await readFirefoxSession(profile) }; } catch { /* not yet */ }
    await delay(500);
  }
  return { waitedMs: Date.now() - started, files: null };
};

const servers = [];

try {
  // 1. The pages, each on a server of its own, served no-store so that a page whose server
  //    has ended cannot come back from Firefox's cache.
  for (const page of ARMS[args.arm] ?? []) {
    const server = await startPageServer({ headers: 'nostore' });
    servers.push({ server, ends: page.ends });
    result.pages.push({ url: server.url, ends: page.ends });
  }

  // 2. The first run: the browser starts on the blank page, the first page opens in it and
  //    every other in a tab of its own, and the browser is closed cleanly, which is when
  //    Firefox writes the session it will restore.
  if (ARMS[args.arm] !== null) {
    const first = child('prepare');
    const prepare = { handshake: await first.handshake(PROTOCOL, 'blank-page-rig'), calls: [] };
    prepare.calls.push(await first.call('browser_tabs', { action: 'list' }, CALL_BOUND_MS));
    for (const [index, page] of result.pages.entries()) {
      prepare.calls.push(index === 0
        ? await first.call('browser_navigate', { url: page.url }, CALL_BOUND_MS)
        : await first.call('browser_tabs', { action: 'new', url: page.url }, CALL_BOUND_MS));
    }
    prepare.calls.push(await first.call('browser_tabs', { action: 'list' }, CALL_BOUND_MS));
    prepare.calls.push(await first.call('browser_close', {}, CALL_BOUND_MS));
    prepare.end = await first.end(30000);
    prepare.session = await readFirefoxSession(profile);
    result.prepare = prepare;
  }

  // 3. The servers that end, end, and the port is proven refused.
  for (const entry of servers.filter((candidate) => candidate.ends)) {
    await entry.server.close();
    entry.refused = await probePort(entry.server.port);
  }
  result.portsAfterTheEnd = servers.filter((entry) => entry.ends).map((entry) => entry.refused);

  // 4. Two resumes, each a new child on the same profile.
  for (const cycle of [1, 2]) {
    const resume = child(`resume-${cycle}`);
    const record = { cycle, handshake: await resume.handshake(PROTOCOL, 'blank-page-rig'), listings: [] };
    const launchedAt = Date.now();

    record.launch = await resume.call('browser_tabs', { action: 'list' }, CALL_BOUND_MS);
    record.launch.tabs = tabs(record.launch);

    // The tabs as the child lists them while the restore reopens them.
    while (Date.now() - launchedAt < POLL_FOR_MS) {
      await delay(POLL_MS);
      const listed = await resume.call('browser_tabs', { action: 'list' }, CALL_BOUND_MS);
      record.listings.push({ afterMs: Date.now() - launchedAt, ms: listed.ms, isError: listed.isError, tabs: tabs(listed) });
    }

    // Which tab Firefox selected, from the session it saves while it runs, and whether the
    // child's current tab, the blank page, is the one Firefox shows.
    record.selectedBeforeTheTidy = await recoveryAfter(launchedAt);
    record.currentBeforeTheTidy = await evaluate(resume);

    // The tidy FirefoxRestore makes: the first reopened page current, then the blank page closed.
    const before = record.listings.at(-1)?.tabs ?? record.launch.tabs;
    if (before.length > 1 && before[0].url === BLANK) {
      const target = before.findIndex((tab, index) => index > 0 && tab.url !== BLANK);
      const select = await resume.call('browser_tabs', { action: 'select', index: target > 0 ? target : 1 }, CALL_BOUND_MS);
      const close = await resume.call('browser_tabs', { action: 'close', index: 0 }, CALL_BOUND_MS);
      record.tidy = { select: { ms: select.ms, isError: select.isError, tabs: tabs(select) }, close: { ms: close.ms, isError: close.isError, tabs: tabs(close) } };
    } else {
      record.tidy = null;
    }

    // The context still answers, on the tab now current, and every other tab is read once:
    // all of them at once, the way the resume reads them, and then one after another, each
    // made current, which is what the one read saves; both are timed, per tab.
    record.currentAfterTheTidy = await evaluate(resume);
    const snapshot = await resume.call('browser_snapshot', {}, CALL_BOUND_MS);
    record.snapshot = { ms: snapshot.ms, isError: snapshot.isError, head: snapshot.text.slice(0, 300) };
    const after = tabs(await resume.call('browser_tabs', { action: 'list' }, CALL_BOUND_MS));
    record.documentRead = await readAll(resume, after.length);
    record.others = [];
    for (const tab of after.filter((candidate) => !candidate.current)) {
      const select = await resume.call('browser_tabs', { action: 'select', index: tab.index }, CALL_BOUND_MS);
      const read = await evaluate(resume);
      record.others.push({ index: tab.index, url: tab.url, title: tab.title, selectMs: select.ms, probe: read, msForThisTab: select.ms + read.ms });
    }
    const current = after.find((tab) => tab.current);
    if (current && record.others.length) await resume.call('browser_tabs', { action: 'select', index: current.index }, CALL_BOUND_MS);
    record.finalTabs = tabs(await resume.call('browser_tabs', { action: 'list' }, CALL_BOUND_MS));
    record.selectedAfterTheTidy = await recoveryAfter(Date.now());

    // A clean close, and what Firefox saved for the next resume.
    record.close = await resume.call('browser_close', {}, CALL_BOUND_MS);
    record.end = await resume.end(30000);
    record.saved = await readFirefoxSession(profile);
    result.cycles.push(record);
    await save();
  }
  result.outcome = 'measured';
} catch (error) {
  result.outcome = `the repetition failed: ${error}`;
} finally {
  for (const entry of servers) await entry.server.close();
  result.finished = iso();
  await save();
  log.write('blank', { arm: args.arm, round: args.round, outcome: result.outcome });
  await log.close();
  process.exit(String(result.outcome).startsWith('the repetition failed') ? 1 : 0);
}
