// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// One replay of a stalled resume, with Playwright's and the browser's own logs on. A
// session's child refuses DEBUG and passes no MOZ_ variable but the one it sets
// (ChildEnvironment.Refused and its allowlist), so this starts the payload's
// @playwright/mcp child itself, the way
// BrowserAI starts a session's child: the same cli.js with --config and --sandbox, the
// same environment block, and the config BrowserAI wrote for that resume, with every path
// of the old session moved into this replay's folder and one change, the variant. The
// browser opens a fresh copy of the profile as it was before the resume, and one call is
// made, which is the call that starts the browser.

import { mkdir, readdir, readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import {
  EventLog, McpClient, callLogOf, childEnvironment, copyTree, isStall, iso, normalisePath, parseArgs, probePort,
  readChromiumSession, readFirefoxSession, required, startPageServer, waitForFile,
} from './lib.mjs';

// as-is: nothing changed. revived: the same address answers again. no-restore: the
// browser restores nothing. blank-first: Playwright's own blank page is put back.
// lazy: Firefox restores a tab only when it is selected. The two '-userjs' variants, added
// after the first replays, write the same preferences into the profile's user.js before
// the launch: Playwright hands firefoxUserPrefs over in Browser.enable, and in the first
// replays the restore went ahead on the value the last launch had left in prefs.js.
const VARIANTS = ['as-is', 'revived', 'no-restore', 'blank-first', 'lazy', 'no-restore-userjs', 'lazy-userjs'];
const USER_JS_PREFERENCES = {
  'no-restore-userjs': { 'browser.sessionstore.resume_session_once': false },
  'lazy-userjs': { 'browser.sessionstore.restore_on_demand': true, 'browser.sessionstore.restore_tabs_lazily': true },
};

// ChildConnection.ChildProtocolVersion at 449fc340.
const PROTOCOL = '2025-11-25';
const CALL_BOUND_MS = 240000;
const DEBUG = 'pw:protocol,pw:browser,pw:api';

// Firefox's log modules for the as-is replay. Each name is first looked for in xul.dll and
// the finding recorded, so that a module that logged nothing can be told from a name this
// build does not have.
const MOZ_LOG_MODULES = ['DocLoader:5', 'nsDocShell:4', 'SessionHistory:4', 'BrowsingContext:4', 'ProcessIsolation:4'];

// Firefox's session store writes its own log at these levels, to the console Playwright
// reads and to sessionstore-logs in the profile; nothing else about the browser changes.
const SESSION_LOG_PREFERENCES = {
  'browser.sessionstore.loglevel': 'All',
  'browser.sessionstore.log.appender.console': 'All',
  'browser.sessionstore.log.appender.dump': 'All',
  'browser.sessionstore.log.appender.file.logOnSuccess': true,
};

const args = required(parseArgs(process.argv.slice(2)),
  'cli', 'browsers', 'registry', 'snapshot', 'config', 'session', 'port', 'token', 'headers', 'family', 'variant', 'out', 'go');
if (!VARIANTS.includes(args.variant)) throw new Error(`unknown variant ${args.variant}`);
await waitForFile(args.go);
await mkdir(args.out, { recursive: true });

const log = new EventLog(join(args.out, 'events.jsonl'));
const firefox = args.family === 'firefox';
const port = Number(args.port);
const result = { variant: args.variant, family: args.family, snapshot: args.snapshot, session: args.session, port, started: iso() };
const save = () => writeFile(join(args.out, 'result.json'), `${JSON.stringify(result, null, 1)}\n`);

// 1. A fresh copy of the profile as it was before the resume.
const profile = join(args.out, 'profile');
await copyTree(join(args.snapshot, 'profile'), profile);
for (const folder of ['output', 'downloads', 'tmp', 'moz']) await mkdir(join(args.out, folder), { recursive: true });

// 2. BrowserAI's config, moved, with the variant's one change.
const oldRoot = normalisePath(args.session);
const move = (value) => {
  if (typeof value !== 'string') return value;
  const spelled = normalisePath(value);
  if (spelled !== oldRoot && !spelled.startsWith(`${oldRoot}\\`)) return value;
  const tail = spelled.slice(oldRoot.length);
  return join(args.out, value.slice(value.length - tail.length));
};
const moveAll = (node) => {
  if (Array.isArray(node)) return node.map(moveAll);
  if (node && typeof node === 'object') return Object.fromEntries(Object.entries(node).map(([key, value]) => [key, moveAll(value)]));
  return move(node);
};
const config = moveAll(JSON.parse(await readFile(args.config, 'utf8')));
const launch = config.browser.launchOptions;
const preferences = (launch.firefoxUserPrefs ??= {});

if (firefox) Object.assign(preferences, SESSION_LOG_PREFERENCES);

switch (args.variant) {
  case 'no-restore':
    if (firefox) preferences['browser.sessionstore.resume_session_once'] = false;
    else launch.args = (launch.args ?? []).filter((argument) => argument !== '--restore-last-session');
    break;
  case 'blank-first':
    launch.ignoreDefaultArgs = (launch.ignoreDefaultArgs ?? []).filter((argument) => argument !== 'about:blank');
    break;
  case 'lazy':
    if (firefox) Object.assign(preferences, { 'browser.sessionstore.restore_on_demand': true, 'browser.sessionstore.restore_tabs_lazily': true });
    else result.notApplicable = 'Chromium has no lazy restore of its own';
    break;
  case 'no-restore-userjs':
  case 'lazy-userjs': {
    if (!firefox) { result.notApplicable = 'a user.js is Firefox\'s'; break; }
    // In Browser.enable too, so that the launch's own preferences agree with the file.
    const chosen = USER_JS_PREFERENCES[args.variant];
    Object.assign(preferences, chosen);
    const lines = Object.entries(chosen).map(([name, value]) => `user_pref(${JSON.stringify(name)}, ${JSON.stringify(value)});`);
    await writeFile(join(profile, 'user.js'), `${lines.join('\n')}\n`);
    result.userJs = lines;
    break;
  }
  default:
    break;
}
if (!firefox && Object.keys(preferences).length === 0) delete launch.firefoxUserPrefs;

const configPath = join(args.out, 'config.json');
await writeFile(configPath, `${JSON.stringify(config, null, 2)}\n`);
result.userDataDir = config.browser.userDataDir;

// 3. The address: answering again for 'revived', and proven refused for every other variant.
let server = null;
if (args.variant === 'revived') {
  try {
    server = await startPageServer({ headers: args.headers, port, token: args.token });
  } catch (error) {
    result.outcome = `the port could not be listened on again: ${error}`;
    await save();
    process.exit(2);
  }
} else {
  result.portBefore = await probePort(port);
  if (result.portBefore.state !== 'refused') {
    result.outcome = `something answers on ${port}, so this is not the state the resume met; nothing was started`;
    await save();
    process.exit(2);
  }
}

// 4. The environment: BrowserAI's block, plus Playwright's debug log, plus Firefox's for as-is.
const extra = { PLAYWRIGHT_BROWSERS_PATH: args.browsers, TEMP: join(args.out, 'tmp'), TMP: join(args.out, 'tmp'), PWTEST_SERVER_REGISTRY: args.registry, DEBUG };
if (firefox && args.variant === 'as-is') {
  const folder = (await readdir(args.browsers)).find((name) => name.startsWith('firefox-'));
  const xul = await readFile(join(args.browsers, folder, 'firefox', 'xul.dll'));
  result.mozLogModules = MOZ_LOG_MODULES.map((module) => ({ module, inXul: xul.includes(Buffer.from(`${module.split(':')[0]}\0`, 'latin1')) }));
  extra.MOZ_LOG = ['timestamp', 'sync', ...MOZ_LOG_MODULES].join(',');
  extra.MOZ_LOG_FILE = join(args.out, 'moz', 'moz.log');
}
const env = childEnvironment(process.env, extra);
result.environmentNames = Object.keys(env).sort();

// 5. The child and its one call.
const child = new McpClient(process.execPath, [args.cli, '--config', configPath, '--sandbox'], {
  cwd: join(args.out, 'output'), env, stderrPath: join(args.out, 'child.stderr.txt'), log, label: 'child',
});
try {
  result.handshake = await child.handshake(PROTOCOL, 'restore-stall-replay');
  result.first = await child.call('browser_tabs', { action: 'list' }, CALL_BOUND_MS);
  result.firstStalled = isStall(result.first);
  result.firstCallLog = result.first.isError ? callLogOf(result.first.text) : null;
  result.outcome = result.firstStalled ? 'the start waited out the launch timeout' : result.first.isError ? 'the call failed another way' : 'the call was answered';
} catch (error) {
  result.outcome = `the replay failed: ${error}`;
} finally {
  result.childEnd = await child.end(30000);
  if (server) {
    result.pageStats = server.stats();
    await server.close();
  }
  result.sessionAfter = firefox ? await readFirefoxSession(profile) : await readChromiumSession(profile, args.token);
  result.finished = iso();
  await save();
  log.write('replay', { variant: args.variant, outcome: result.outcome, ms: result.first?.ms ?? null });
  await log.close();

  // A child still running holds this process open through its pipes; the step's job ends
  // it once this process has gone.
  process.exit(String(result.outcome).startsWith('the replay failed') ? 1 : 0);
}
