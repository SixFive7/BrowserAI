// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The measurement through BrowserAI, driven the way a client drives it: the published
// BrowserAI.exe started as a developer starts a background for a build that is not
// installed, a relay started the way a client starts one, and every call made over the
// relay's standard streams. Each repetition makes a session, opens a local page in it,
// lets BrowserAI close the browser on its idle time, ends the page's server or keeps it,
// resumes the session and times the first browser call, which is the call that starts the
// browser. run.ps1 starts this inside a job of its own and ends that job afterwards.

import { spawn } from 'node:child_process';
import { createWriteStream } from 'node:fs';
import { appendFile, copyFile, mkdir, readdir, readFile } from 'node:fs/promises';
import { randomBytes } from 'node:crypto';
import { dirname, join } from 'node:path';
import {
  EventLog, McpClient, askBackgroundToStop, browserAiEnvironment, callLogOf, copyTree, delay, isStall, iso, measureTree,
  normalisePath, parseArgs, probePort, readChromiumSession, readFirefoxSession, required, startPageServer, waitForFile,
} from './lib.mjs';

// Every arm the rig knows. The plan names which of them a batch runs.
const ARMS = {
  'ff-dead': { family: 'firefox', headers: 'dashboard', serverEnds: true },
  'ff-alive': { family: 'firefox', headers: 'dashboard', serverEnds: false },
  'ff-dead-plain': { family: 'firefox', headers: 'plain', serverEnds: true },
  'cr-dead': { family: 'chromium', headers: 'dashboard', serverEnds: true },
  'cr-alive': { family: 'chromium', headers: 'dashboard', serverEnds: false },
  // Added after the first batch, whose ff-dead-plain page came back from the cache.
  'ff-dead-nostore': { family: 'firefox', headers: 'nostore', serverEnds: true },
  'ff-dead-cacheable': { family: 'firefox', headers: 'cacheable', serverEnds: true },
  // Added after the first batch too: what the resume BrowserAI's answer asks for meets.
  'ff-dead-again': { family: 'firefox', headers: 'dashboard', serverEnds: true, again: true },
  // Added after the second batch: the same, with the second resume asked again while refused.
  'ff-dead-again-wait': { family: 'firefox', headers: 'dashboard', serverEnds: true, again: true, retry: true },
};

// BrowserProxy.CallerProtocolVersion at 449fc340.
const PROTOCOL = '2025-11-25';

// Past Playwright's 180 s launch timeout, so that a start that waits it out is answered
// by BrowserAI and is not cut off by this client.
const CALL_BOUND_MS = 240000;

const args = required(parseArgs(process.argv.slice(2)),
  'exe', 'data-root', 'registry', 'client-config', 'sessions', 'out', 'arms', 'rounds', 'idle-minutes', 'retry-after-stall', 'warmup', 'go');
await waitForFile(args.go);
await mkdir(args.out, { recursive: true });

const log = new EventLog(join(args.out, 'events.jsonl'));
const rounds = Number(args.rounds);

// An arm is named alone, for --rounds rounds, or as name:N, for its first N rounds only.
const armRounds = new Map(args.arms.split(',').filter(Boolean).map((entry) => {
  const [name, count] = entry.split(':');
  return [name, count === undefined ? rounds : Number(count)];
}));
const arms = [...armRounds.keys()];
for (const arm of arms) if (!ARMS[arm]) throw new Error(`unknown arm ${arm}`);
const idleMinutes = Number(args['idle-minutes']);
const retryAfterStall = Number(args['retry-after-stall']);
const settings = { headed: false, transcript: false, captureNetwork: false, idleMinutes, debug: true };

const payload = join(dirname(args.exe), 'payload');
const packageVersion = async (...parts) => JSON.parse(await readFile(join(payload, 'mcp', 'node_modules', ...parts, 'package.json'), 'utf8')).version;
const pins = JSON.parse(await readFile(join(payload, 'mcp', 'node_modules', 'playwright-core', 'browsers.json'), 'utf8')).browsers;
log.write('versions', {
  node: process.version,
  mcp: await packageVersion('@playwright', 'mcp'),
  playwrightCore: await packageVersion('playwright-core'),
  browsers: pins.filter((pin) => ['chromium', 'firefox'].includes(pin.name)).map(({ name, revision, browserVersion }) => ({ name, revision, browserVersion })),
  arms, armRounds: Object.fromEntries(armRounds), rounds, idleMinutes, retryAfterStall, warmup: args.warmup,
});

const env = browserAiEnvironment(process.env, {
  PWTEST_SERVER_REGISTRY: args.registry,
  CLAUDE_CONFIG_DIR: join(args['client-config'], 'claude'),
  CODEX_HOME: join(args['client-config'], 'codex'),
});
await mkdir(env.CLAUDE_CONFIG_DIR, { recursive: true });
await mkdir(env.CODEX_HOME, { recursive: true });

// The background, on a pipe of the rig's own, as the suite's harness names one.
const pipe = `\\\\.\\pipe\\BrowserAI-Background-restore-stall-${process.pid}-${randomBytes(6).toString('hex')}`;
const backgroundStdout = createWriteStream(join(args.out, 'background.stdout.txt'), { flags: 'a' });
const backgroundStderr = createWriteStream(join(args.out, 'background.stderr.txt'), { flags: 'a' });
backgroundStdout.on('error', (error) => log.write('file-error', { error: String(error) }));
backgroundStderr.on('error', (error) => log.write('file-error', { error: String(error) }));
const background = spawn(args.exe, ['--background', '--pipe', pipe, '--data-root', args['data-root']], {
  cwd: args['data-root'], env, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true,
});
let backgroundGone = false;
background.once('error', (error) => { backgroundGone = true; log.write('background-error', { error: String(error) }); });
background.once('exit', (code, signal) => { backgroundGone = true; log.write('background-exit', { code, signal }); });
const backgroundClosed = new Promise((resolve) => background.once('close', (code, signal) => resolve({ code, signal })));
background.stdout.pipe(backgroundStdout);

// A session's own records go to the background's standard error, and these three are how
// an idle close ends (BrowserIdleTimer's IdleLog, events 60, 63 and 62).
const sessionEnds = [];
let unread = '';
background.stderr.setEncoding('utf8');
background.stderr.on('data', (chunk) => {
  backgroundStderr.write(chunk);
  unread += chunk;
  const pattern = /Browser server ended after (.+?) idle on the session at (.+?), node child included|The session at (.+?) was idle for (.+?) with no browser up|The idle close on the session at (.+?) failed/g;
  let consumed = 0;
  for (let match = pattern.exec(unread); match !== null; match = pattern.exec(unread)) {
    const kind = match[2] !== undefined ? 'closed' : match[3] !== undefined ? 'nothing-to-close' : 'failed';
    sessionEnds.push({ at: Date.now(), kind, session: match[2] ?? match[3] ?? match[5], line: match[0] });
    consumed = pattern.lastIndex;
  }
  unread = unread.slice(consumed);
  if (unread.length > 65536) unread = unread.slice(-8192);
});
log.write('background', { pid: background.pid, pipe });

const relay = new McpClient(args.exe, ['--mcp', '--pipe', pipe, '--data-root', args['data-root']], {
  cwd: args.sessions, env, stderrPath: join(args.out, 'relay.stderr.txt'), log, label: 'relay',
});
const opened = await relay.handshake(PROTOCOL, 'restore-stall-rig');
log.write('relay', { pid: relay.pid, server: opened.server, protocolVersion: opened.protocolVersion, tools: opened.tools.length });
for (const tool of ['browserai_init', 'browserai_resume', 'browserai_destroy', 'browserai_list', 'browser_navigate', 'browser_tabs', 'browser_snapshot']) {
  if (!opened.tools.includes(tool)) throw new Error(`the relay lists no ${tool}`);
}

const call = (tool, argumentsObject, bound = CALL_BOUND_MS) => relay.call(tool, argumentsObject, bound);

// The relay holds a call until the background answers; a listing is the first proof it does.
let ready = null;
for (let attempt = 1; attempt <= 12 && !ready; attempt++) {
  const answer = await call('browserai_list', { directory: args.sessions }, 60000);
  if (!answer.isError) ready = answer;
  else await delay(5000);
}
if (!ready) throw new Error('the background did not answer browserai_list within twelve tries');
log.write('ready', { listing: ready.text.slice(0, 200) });

async function waitForSessionEnd(dir, since, limitMs) {
  const wanted = normalisePath(dir);
  const started = Date.now();
  for (;;) {
    const found = sessionEnds.find((end) => end.at >= since && normalisePath(end.session) === wanted);
    if (found) return found;
    if (backgroundGone || Date.now() - started > limitMs) return null;
    await delay(500);
  }
}

// Every config BrowserAI wrote for this session's child, from the background's instance
// folder, before the session's own close deletes it. The first one copied after the
// resume is also kept as <name>.json, the config replay.mjs launches from.
const configNamed = new Set();

async function copyConfigs(name, label) {
  const instances = join(args['data-root'], 'instances');
  const copied = [];
  let folders = [];
  try { folders = (await readdir(instances)).filter((folder) => folder.startsWith(`${background.pid}-`)); } catch { return copied; }
  for (const folder of folders) {
    let files = [];
    try { files = (await readdir(join(instances, folder))).filter((file) => /^playwright-mcp-.*\.json$/.test(file)); } catch { continue; }
    for (const file of files) {
      await mkdir(join(args.out, 'configs'), { recursive: true });
      const target = join(args.out, 'configs', `${name}.${label}.${file}`);
      await copyFile(join(instances, folder, file), target);
      if (!configNamed.has(name)) {
        await copyFile(target, join(args.out, 'configs', `${name}.json`));
        configNamed.add(name);
      }
      copied.push(target);
    }
  }
  return copied;
}

// The profile's size every few seconds while the first call is in flight: a start that
// stops early leaves a profile that stops growing.
function sampleProfile(profile, everyMs) {
  const samples = [];
  let running = true;
  const loop = (async () => {
    while (running) {
      const started = Date.now();
      samples.push({ t: iso(), ...(await measureTree(profile)) });
      await delay(Math.max(0, everyMs - (Date.now() - started)));
    }
  })();
  return async () => { running = false; await loop; return samples; };
}

const readSession = (family, profile, token) => (family === 'firefox' ? readFirefoxSession(profile) : readChromiumSession(profile, token));
const stalls = {};

async function repetition(arm, round) {
  const spec = ARMS[arm];
  const name = `${arm}-${round}`;
  const dir = join(args.sessions, name);
  const profile = join(dir, 'profile');
  const row = { name, arm, round, ...spec, idleMinutes, dir, started: iso() };
  const server = await startPageServer({ headers: spec.headers });
  Object.assign(row, { port: server.port, url: server.url, token: server.token });
  log.write('repetition', { name, url: server.url });

  try {
    row.init = await call('browserai_init', { directory: dir, purpose: `restore-stall rig, ${arm}, round ${round}`, browser: spec.family, ...settings });
    if (row.init.isError) return (row.outcome = 'init refused');

    row.navigate = await call('browser_navigate', { session: dir, url: server.url, why: 'opening the page this session will be resumed on' });
    if (row.navigate.isError) return (row.outcome = 'navigate failed');
    const navigated = Date.now();

    await delay(10000);
    row.pageBeforeClose = server.stats();

    const end = await waitForSessionEnd(dir, navigated, (idleMinutes * 60000) + 120000);
    row.idleClose = end ? { kind: end.kind, afterNavigateMs: end.at - navigated, line: end.line } : null;
    if (!end || end.kind !== 'closed') return (row.outcome = end ? `idle close: ${end.kind}` : 'no idle close seen');

    if (spec.serverEnds) await server.close();
    row.portAtResume = await probePort(server.port);

    try {
      await copyTree(profile, join(args.out, 'snapshots', name, 'profile'));
      row.snapshot = join(args.out, 'snapshots', name);
    } catch (error) {
      row.snapshotError = String(error);
    }

    row.sessionBefore = await readSession(spec.family, profile, server.token);
    row.profileBefore = await measureTree(profile);
    await delay(5000);

    row.resume = await call('browserai_resume', { directory: dir, why: 'resuming the session after its idle close', ...settings });
    if (row.resume.isError) return (row.outcome = 'resume refused');
    row.configsAfterResume = await copyConfigs(name, 'resume');

    const stopSampling = sampleProfile(profile, 5000);
    row.first = await call('browser_tabs', { session: dir, action: 'list', why: 'the first browser call after the resume, the one that starts the browser' });
    row.profileDuringFirst = await stopSampling();
    row.firstStalled = isStall(row.first);
    row.firstCallLog = row.first.isError ? callLogOf(row.first.text) : null;
    row.configsAfterFirst = await copyConfigs(name, 'first');
    stalls[arm] = (stalls[arm] ?? 0) + (row.firstStalled ? 1 : 0);

    // After a stall, the next call starts the browser again; the first stalls of an arm
    // are followed by one to see whether a second start gets through.
    if (!row.firstStalled || stalls[arm] <= retryAfterStall) {
      row.second = await call('browser_snapshot', { session: dir, why: 'the call after the first, to see whether the session answers now' });
      row.secondStalled = isStall(row.second);
      row.secondCallLog = row.second.isError ? callLogOf(row.second.text) : null;
    }

    // After a stall, BrowserAI's answer to the next call asks for a resume; this arm makes
    // it and times the first call after it the same way.
    if (spec.again && row.firstStalled) {
      row.againResume = await call('browserai_resume', { directory: dir, why: 'resuming again, as the answer after the stall asks', ...settings });
      // Added after the second batch: there, a Firefox that had started 36 ms into the call
      // before still held the profile when that resume came, and the resume was refused. This
      // arm asks again every five seconds while the refusal says so, as a caller reading it
      // would, for a minute.
      row.againRefusals = 0;
      while (spec.retry && row.againResume.isError && /is held open by another process/.test(row.againResume.text) && row.againRefusals < 12) {
        row.againRefusals++;
        await delay(5000);
        row.againResume = await call('browserai_resume', { directory: dir, why: 'resuming again once the profile is free', ...settings });
      }
      if (!row.againResume.isError) {
        row.againFirst = await call('browser_tabs', { session: dir, action: 'list', why: 'the first browser call after the second resume' });
        row.againStalled = isStall(row.againFirst);
      }
    }

    row.sessionAfter = await readSession(spec.family, profile, server.token);
    row.outcome = row.firstStalled ? 'the first start waited out the launch timeout' : row.first.isError ? 'the first call failed another way' : 'the first call was answered';
    return row.outcome;
  } finally {
    row.destroy = await call('browserai_destroy', { directory: dir, why: 'the repetition is recorded' }, 120000);
    await server.close();
    row.finished = iso();
    await appendFile(join(args.out, 'results.jsonl'), `${JSON.stringify(row)}\n`);
    log.write('result', { name, outcome: row.outcome ?? 'threw', firstMs: row.first?.ms ?? null, stalled: row.firstStalled ?? null });
  }
}

// One fresh session per family before the counted ones: the first start from a new copy
// of a browser pays for that copy alone, and is recorded beside the counts and not in them.
async function warmUp(family) {
  const name = `warmup-${family}`;
  const dir = join(args.sessions, name);
  const server = await startPageServer({ headers: 'dashboard' });
  const row = { name, family, dir, started: iso() };
  try {
    row.init = await call('browserai_init', { directory: dir, purpose: `restore-stall rig, the warm-up of ${family}`, browser: family, ...settings });
    if (!row.init.isError) row.navigate = await call('browser_navigate', { session: dir, url: server.url, why: 'a first start from the scratch copy of this browser, kept out of the counts' });
  } finally {
    row.destroy = await call('browserai_destroy', { directory: dir, why: 'the warm-up is over' }, 120000);
    await server.close();
    row.finished = iso();
    await appendFile(join(args.out, 'warmup.jsonl'), `${JSON.stringify(row)}\n`);
    log.write('warmup', { family, navigateMs: row.navigate?.ms ?? null, isError: row.navigate?.isError ?? null });
  }
}

let failed = false;

try {
  if (args.warmup === 'yes') {
    for (const family of [...new Set(arms.map((arm) => ARMS[arm].family))]) await warmUp(family);
  }

  // Interleaved arm by arm, so that whatever drifts on the machine meets every arm alike.
  for (let round = 1; round <= rounds && !backgroundGone; round++) {
    for (const arm of arms) {
      if (backgroundGone) break;
      if (round > armRounds.get(arm)) continue;
      try {
        await repetition(arm, round);
      } catch (error) {
        log.write('repetition-error', { arm, round, error: String(error), stack: error?.stack?.slice(0, 2000) ?? null });
      }
      await delay(3000);
    }
  }
} catch (error) {
  failed = true;
  log.write('rig-error', { error: String(error), stack: error?.stack?.slice(0, 2000) ?? null });
} finally {
  // The relay is told to end by its input closing, as a client ends it; the background is
  // asked to stop the way the uninstall hook asks it, and what is left after the bound
  // ends with the step's job once this process has gone.
  log.write('ending', { backgroundGone, stalls });
  log.write('relay-end', await relay.end(30000));
  if (!backgroundGone) log.write('background-stop', await askBackgroundToStop(pipe));
  const ended = await Promise.race([backgroundClosed, delay(120000).then(() => null)]);
  log.write('background-end', ended ?? { stillRunning: true });
  if (!ended) {
    background.stdout.unpipe(backgroundStdout);
    background.stdout.destroy();
    background.stderr.destroy();
    background.unref();
  }
  await new Promise((resolve) => backgroundStderr.end(resolve));
  if (!backgroundStdout.writableEnded) await new Promise((resolve) => backgroundStdout.end(resolve));
  await log.close();
  process.exit(failed ? 1 : 0);
}
