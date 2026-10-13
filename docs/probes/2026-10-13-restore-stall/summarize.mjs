// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// node summarize.mjs <batch folder> ...: the tables the kb entry is written from, read
// from the files the rig wrote and from nothing else. A measure batch gives one row per
// arm, with the exact two-sided Fisher p of each arm against ff-dead and an exact 95%
// interval on each arm's rate; a replay batch gives each replay's outcome and, from
// Playwright's protocol log, which of the three waits of a persistent launch was still
// open when it ended. Each summary is printed and written to <batch>\summary.txt.

import { readdir, readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { normalisePath } from './lib.mjs';

const readLines = async (path) => {
  try { return (await readFile(path, 'utf8')).split(/\r?\n/).filter((line) => line.trim()); } catch { return []; }
};

// A line cut short by a process that ended mid-write is passed over and counted.
let unreadableLines = 0;
const readJsonLines = async (path) => (await readLines(path)).flatMap((line) => {
  try { return [JSON.parse(line)]; } catch { unreadableLines++; return []; }
});

// The census's times are .NET's, with seven decimals; a JavaScript time has three.
const timeOf = (text) => Date.parse(String(text).replace(/(\.\d{3})\d+/, '$1'));
const median = (values) => {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
};
const span = (values) => (values.length === 0 ? '-' : `${Math.min(...values)} / ${median(values)} / ${Math.max(...values)} ms`);

const logFactorial = (() => { const cache = [0, 0]; return (k) => { for (let i = cache.length; i <= k; i++) cache[i] = cache[i - 1] + Math.log(i); return cache[k]; }; })();

// The exact two-sided Fisher test of a 2x2 table [[a, b], [c, d]].
function fisher(a, b, c, d) {
  const n = a + b + c + d;
  const row = a + b;
  const column = a + c;
  const p = (x) => Math.exp(logFactorial(row) + logFactorial(n - row) + logFactorial(column) + logFactorial(n - column)
    - logFactorial(n) - logFactorial(x) - logFactorial(row - x) - logFactorial(column - x) - logFactorial(n - row - column + x));
  const observed = p(a);
  let total = 0;
  for (let x = Math.max(0, column - (n - row)); x <= Math.min(row, column); x++) {
    const px = p(x);
    if (px <= observed * (1 + 1e-9)) total += px;
  }
  return Math.min(1, total);
}

// The exact (Clopper-Pearson) 95% interval on k of n, by bisection on the binomial tail.
function interval(k, n) {
  const tail = (p, atLeast) => {
    let sum = 0;
    for (let x = 0; x <= n; x++) {
      if (atLeast ? x < k : x > k) continue;
      sum += Math.exp(logFactorial(n) - logFactorial(x) - logFactorial(n - x) + (x * Math.log(p)) + ((n - x) * Math.log(1 - p)));
    }
    return sum;
  };
  const solve = (atLeast) => {
    let low = 1e-12;
    let high = 1 - 1e-12;
    for (let step = 0; step < 100; step++) {
      const middle = (low + high) / 2;
      const value = tail(middle, atLeast);
      if (atLeast ? value < 0.025 : value > 0.025) low = middle; else high = middle;
    }
    return (low + high) / 2;
  };
  return [k === 0 ? 0 : solve(true), k === n ? 1 : solve(false)];
}

// The census: every process's image and command line, keyed by pid and creation time, and
// the snapshots in order.
async function readCensus(path) {
  const described = new Map();
  const snapshots = [];
  for (const snapshot of await readJsonLines(path)) {
    for (const member of snapshot.procs) {
      const key = `${member.pid}@${member.created}`;
      if (member.exe !== undefined) described.set(key, { exe: member.exe, cmd: member.cmd });
    }
    snapshots.push(snapshot);
  }
  return { described, snapshots };
}

// What the census saw of one session's browser while a call was in flight: the processes
// whose command line names the session's profile, and their descendants by parent pid.
// Nothing is picked by an image name.
function browserDuring(census, profile, from, to) {
  const wanted = normalisePath(profile);
  const [start, end] = [timeOf(from), timeOf(to)];
  const inWindow = census.snapshots.filter((snapshot) => timeOf(snapshot.t) >= start && timeOf(snapshot.t) <= end);
  const members = new Map();
  for (const snapshot of inWindow) {
    for (const member of snapshot.procs) {
      const key = `${member.pid}@${member.created}`;
      const entry = members.get(key) ?? { pid: member.pid, ppid: member.ppid, cmd: census.described.get(key)?.cmd ?? '', firstCpuMs: member.cpuMs, lastCpuMs: member.cpuMs, samples: 0 };
      entry.lastCpuMs = member.cpuMs;
      entry.samples++;
      members.set(key, entry);
    }
  }
  const chosen = new Set([...members.values()].filter((entry) => entry.cmd.toLowerCase().replace(/\//g, '\\').includes(wanted)).map((entry) => entry.pid));
  for (let grew = true; grew;) {
    grew = false;
    for (const entry of members.values()) {
      if (!chosen.has(entry.pid) && chosen.has(entry.ppid)) { chosen.add(entry.pid); grew = true; }
    }
  }
  const all = [...members.values()].filter((entry) => chosen.has(entry.pid));
  // The browser process is the one the content processes hang from; a launcher process
  // that started it carries the same switches and no children of that kind.
  const children = all.filter((entry) => /-contentproc|--type=/.test(entry.cmd));
  const candidates = all.filter((entry) => !/-contentproc|--type=/.test(entry.cmd) && /-juggler-pipe|--remote-debugging-pipe/.test(entry.cmd));
  const main = candidates.find((entry) => children.some((child) => child.ppid === entry.pid)) ?? candidates[candidates.length - 1] ?? null;
  return {
    snapshots: inWindow.length,
    processes: all.length,
    contentProcesses: all.filter((entry) => /-contentproc|--type=renderer/.test(entry.cmd)).length,
    safeMode: all.some((entry) => /\s-safeMode(\s|$)/.test(entry.cmd)),
    mainPid: main?.pid ?? null,
    mainCpuMs: main ? main.lastCpuMs - main.firstCpuMs : null,
  };
}

const restoresTheUrl = (row) => (row.family === 'firefox'
  ? (row.sessionBefore ?? []).some((file) => (file.windows ?? []).some((window) => window.tabs.some((tab) => tab.entries.some((entry) => String(entry).includes(row.token)))))
  : (row.sessionBefore?.files ?? []).some((file) => file.hasToken));

async function summariseMeasure(batch, lines) {
  const folder = join(batch, 'measure');
  const rows = await readJsonLines(join(folder, 'results.jsonl'));
  const warmups = await readJsonLines(join(folder, 'warmup.jsonl'));
  const events = await readJsonLines(join(folder, 'events.jsonl'));
  const census = await readCensus(join(folder, 'census.jsonl'));
  const versions = events.find((event) => event.event === 'versions');
  lines.push(`versions: ${JSON.stringify(versions ?? null)}`);
  lines.push(`build: ${(await readLines(join(batch, 'build.txt'))).join(' | ')}`);
  for (const warmup of warmups) lines.push(`warm-up ${warmup.family}: navigate ${warmup.navigate?.ms ?? '-'} ms, isError ${warmup.navigate?.isError ?? '-'}`);

  const arms = [...new Set(rows.map((row) => row.arm))];
  const counted = (arm) => rows.filter((row) => row.arm === arm && row.first !== undefined);
  const stalledIn = (arm) => counted(arm).filter((row) => row.firstStalled).length;
  lines.push('');
  lines.push('| Arm | Resumes measured | First start waited out 180 s | 95% interval | Fisher p against ff-dead | Answered first starts, min / median / max | Stalled starts | After a stall, the second call | Restore named the page | Safe mode seen |');
  lines.push('|---|--:|--:|---|--:|---|---|---|--:|--:|');
  for (const arm of arms) {
    const measured = counted(arm);
    const stalled = measured.filter((row) => row.firstStalled);
    const answered = measured.filter((row) => !row.firstStalled && !row.first.isError);
    const [low, high] = interval(stalled.length, measured.length);
    const against = arm === 'ff-dead' || !arms.includes('ff-dead') ? '-'
      : fisher(stalledIn('ff-dead'), counted('ff-dead').length - stalledIn('ff-dead'), stalled.length, measured.length - stalled.length).toFixed(4);
    const seconds = stalled.filter((row) => row.second).map((row) => (row.secondStalled ? 'stalled' : row.second.isError ? 'failed' : 'answered'));
    const views = measured.map((row) => browserDuring(census, join(row.dir, 'profile'), row.first.startedAt, row.first.answeredAt));
    lines.push(`| ${arm} | ${measured.length} | ${stalled.length} | ${low.toFixed(2)} to ${high.toFixed(2)} | ${against} | ${span(answered.map((row) => row.first.ms))} | ${span(stalled.map((row) => row.first.ms))} | ${seconds.join(', ') || '-'} | ${measured.filter(restoresTheUrl).length} of ${measured.length} | ${views.filter((view) => view.safeMode).length} |`);
  }

  lines.push('');
  for (const row of rows) {
    const view = row.first ? browserDuring(census, join(row.dir, 'profile'), row.first.startedAt, row.first.answeredAt) : null;
    lines.push(`${row.name}: ${row.outcome ?? 'threw'}; idle close ${row.idleClose?.afterNavigateMs ?? '-'} ms after the navigation; port at resume ${row.portAtResume?.state ?? '-'}; first ${row.first?.ms ?? '-'} ms; second ${row.second?.ms ?? '-'} ms ${row.second ? (row.secondStalled ? 'stalled' : row.second.isError ? 'failed' : 'answered') : ''}; browser in the census: ${view ? JSON.stringify(view) : '-'}`);
    if (row.firstStalled && row.firstCallLog) lines.push(`  call log, last line: ${row.firstCallLog.replace(/\u001b\[[0-9;]*[A-Za-z]/g, '').trim().split('\n').pop().trim()}`);
    if (row.againResume) lines.push(`  resumed again${row.againRefusals ? ` after ${row.againRefusals} refusal(s) five seconds apart` : ''}: ${row.againResume.isError ? `refused: ${row.againResume.text.replace(/\s+/g, ' ').slice(0, 120)}` : `first call ${row.againFirst?.ms ?? '-'} ms, ${row.againStalled ? 'waited out the launch timeout again' : row.againFirst?.isError ? 'failed another way' : 'answered'}`}`);
  }
}

// Playwright's protocol log: one message per line, after a time and the 'pw:protocol' name.
function readProtocol(text) {
  const messages = [];
  for (const line of text.split(/\r?\n/)) {
    const match = line.match(/^(\S+) pw:protocol (.*?)(\{.*\})\s*$/);
    if (!match) continue;
    try { messages.push({ t: match[1], sent: match[2].includes('SEND'), message: JSON.parse(match[3]) }); } catch { /* a cut line */ }
  }
  return messages;
}

// Which wait of loadDefaultContextAsIs was still open: the first page, its Page.ready, or
// its main frame's load, as Playwright's Firefox page reads them.
function openWait(messages) {
  const pages = messages.filter((entry) => !entry.sent && entry.message.method === 'Browser.attachedToTarget' && entry.message.params?.targetInfo?.type === 'page');
  if (pages.length === 0) return { wait: 'no page was attached: the wait for the first page', pages: 0 };
  const session = pages[0].message.params.sessionId;
  const ofPage = messages.filter((entry) => !entry.sent && entry.message.sessionId === session);
  const ready = ofPage.some((entry) => entry.message.method === 'Page.ready');
  const mainFrame = ofPage.find((entry) => entry.message.method === 'Page.frameAttached' && !entry.message.params?.parentFrameId)?.message.params.frameId ?? null;
  const loaded = ofPage.some((entry) => entry.message.method === 'Page.eventFired' && entry.message.params?.name === 'load' && (!mainFrame || entry.message.params.frameId === mainFrame));
  // An event has a method; an answer to a command sent on the page's session has an id.
  const events = ofPage.map((entry) => {
    const { method, params, id } = entry.message;
    if (method === undefined) return `${entry.t.slice(11, 23)} (the answer to command ${id})`;
    const detail = params?.url ?? params?.errorText ?? params?.name ?? '';
    return `${entry.t.slice(11, 23)} ${method}${detail ? ` ${String(detail).slice(0, 120)}` : ''}`;
  });
  const wait = !ready ? 'the first page never reported Page.ready: the wait for the page to initialise'
    : !loaded ? "Page.ready came and the main frame's load never did: the wait for the load state"
      : 'every one of the three waits was answered';
  return { wait, pages: pages.length, url: pages[0].message.params.targetInfo.url ?? null, events };
}

async function summariseReplays(batch, lines) {
  const folders = (await readdir(batch)).filter((name) => name.startsWith('replay-')).sort();
  for (const name of folders) {
    let result;
    try { result = JSON.parse(await readFile(join(batch, name, 'result.json'), 'utf8')); } catch { lines.push(`${name}: no result.json`); continue; }
    lines.push('');
    lines.push(`${name}: ${result.outcome}; ${result.first?.ms ?? '-'} ms; child end ${JSON.stringify(result.childEnd ?? null)}`);
    if (result.mozLogModules) lines.push(`  MOZ_LOG modules found in xul.dll: ${result.mozLogModules.map((entry) => `${entry.module} ${entry.inXul ? 'yes' : 'NO'}`).join(', ')}`);
    const stderr = await readFile(join(batch, name, 'child.stderr.txt'), 'utf8').catch(() => '');
    const wait = openWait(readProtocol(stderr));
    lines.push(`  ${wait.wait}; page targets ${wait.pages}; first page at ${wait.url ?? '-'}`);
    for (const event of (wait.events ?? []).slice(0, 40)) lines.push(`    ${event}`);
    const sessionStore = stderr.split(/\r?\n/).filter((line) => /SessionStore/.test(line)).slice(0, 30);
    for (const line of sessionStore) lines.push(`  ${line.slice(0, 300)}`);
  }
}

for (const batch of process.argv.slice(2)) {
  const lines = [`# ${batch}`];
  const entries = await readdir(batch);
  unreadableLines = 0;
  if (entries.includes('measure')) await summariseMeasure(batch, lines);
  if (entries.some((name) => name.startsWith('replay-'))) await summariseReplays(batch, lines);
  lines.push('', `lines passed over as unreadable JSON: ${unreadableLines}`);
  const text = `${lines.join('\n')}\n`;
  process.stdout.write(text);
  await writeFile(join(batch, 'summary.txt'), text);
}
