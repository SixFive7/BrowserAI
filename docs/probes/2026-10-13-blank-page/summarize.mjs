// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// The tables of one batch: per repetition and resume, how long the launch took, what the
// child listed first, when each reopened tab came up and with which title, which tab Firefox
// had selected, what the tidy did and whether the context still answered, and what Firefox
// saved for the next resume; and per arm, whether the second resume met the set the first
// left. For a 'repro' batch, the reproduction's rows.

import { readdir, readFile } from 'node:fs/promises';
import { join } from 'node:path';

const batch = process.argv[2];
if (!batch) throw new Error('usage: summarize.mjs <batch folder>');

const read = async (path) => { try { return JSON.parse(await readFile(path, 'utf8')); } catch { return null; } };
const names = (await readdir(batch, { withFileTypes: true })).filter((entry) => entry.isDirectory()).map((entry) => entry.name).sort();
const out = [];
const line = (text = '') => out.push(text);

line(`# ${batch}`);

const navigate = await read(join(batch, 'navigate', 'result.json'));
if (navigate) {
  line();
  line('| Variant | The navigation | URL after it | Wait for load | ms | document.documentURI |');
  line('|---|---|---|---|--:|---|');
  for (const row of navigate.rows) line(`| ${row.name} | ${row.navigation} | ${row.urlAfter} | ${row.load} | ${row.loadMs} | ${String(row.documentURI).slice(0, 60)} |`);
}

const repro = await read(join(batch, 'repro', 'result.json'));
if (repro) {
  line();
  line('| Variant | Outcome | ms | Pages, or the load wait |');
  line('|---|---|--:|---|');
  for (const row of repro.rows) {
    if ('load' in row) line(`| ${row.name} | goto: ${row.goto}; load: ${row.load} | ${row.loadMs} | ${row.pageUrl} |`);
    else line(`| ${row.name} | ${row.outcome} | ${row.ms} | ${(row.pages ?? []).join(', ')} |`);
  }
}

for (const name of names.filter((each) => each.startsWith('issue-'))) {
  let printed;
  try { printed = await readFile(join(batch, name, 'node.stdout.txt'), 'utf8'); } catch { printed = '(no node.stdout.txt)'; }
  line();
  line(`${name}, what the script printed:`);
  line();
  line('```');
  line(printed.trimEnd());
  line('```');
}

const urlsOf = (tabs) => (tabs ?? []).map((tab) => `${tab.current ? '*' : ''}${tab.url === 'about:blank' ? 'blank' : tab.url.replace(/^http:\/\/127\.0\.0\.1:(\d+)\/[^?]*/, ':$1')}${tab.title ? ` "${tab.title}"` : ''}`).join(', ');
const savedOf = (files) => {
  const clean = (files ?? []).find((file) => file.file === 'sessionstore.jsonlz4');
  if (!clean || !clean.windows) return 'none';
  return clean.windows.map((window) => `selected ${window.selected} of [${window.tabs.map((tab) => (tab.entries.at(-1) ?? '?').replace(/^http:\/\/127\.0\.0\.1:(\d+)\/[^?]*/, ':$1')).join(', ')}]`).join('; ');
};
const recoveryOf = (probe) => {
  const recovery = (probe?.files ?? []).find((file) => file.file === 'sessionstore-backups/recovery.jsonlz4');
  if (!recovery || !recovery.windows) return `none after ${probe?.waitedMs ?? '?'} ms`;
  return recovery.windows.map((window) => `selected ${window.selected} of [${window.tabs.map((tab) => (tab.entries.at(-1) ?? '?').replace(/^http:\/\/127\.0\.0\.1:(\d+)\/[^?]*/, ':$1').replace('about:blank', 'blank')).join(', ')}]`).join('; ');
};

// The repetitions are the folders of a 'blank' batch; the other stages' batches have none.
const repetitions = [];
for (const name of names) if ((await read(join(batch, name, 'result.json')))?.arm) repetitions.push(name);
if (repetitions.length === 0) {
  process.stdout.write(`${out.join('\n')}\n`);
  process.exit(0);
}

const byArm = new Map();
line();
line('| Repetition | Resume | Launch ms | First list | First reopened tab listed after | Listed last before the tidy | Firefox selected before the tidy | Current before | Tidy | Current after | Snapshot | Others | Saved after the close |');
line('|---|--:|--:|---|--:|---|---|---|---|---|---|---|---|');

for (const name of repetitions) {
  const result = await read(join(batch, name, 'result.json'));
  if (!result) continue;
  if (!byArm.has(result.arm)) byArm.set(result.arm, []);
  byArm.get(result.arm).push(result);
  for (const cycle of result.cycles ?? []) {
    const reopened = cycle.listings.find((listing) => listing.tabs.length > 1);
    const last = cycle.listings.at(-1)?.tabs;
    const visibility = (probe) => probe?.value ? `${probe.value.visibility}, ${probe.value.documentURI.slice(0, 40)}` : `error: ${(probe?.text ?? '').slice(0, 60)}`;
    const tidy = cycle.tidy ? `select ${cycle.tidy.select.isError ? 'failed' : 'ok'}, close ${cycle.tidy.close.isError ? 'failed' : 'ok'} -> ${urlsOf(cycle.tidy.close.tabs)}` : 'nothing closed';
    const others = (cycle.others ?? []).map((other) => `${other.url.replace(/^http:\/\/127\.0\.0\.1:(\d+)\/[^?]*/, ':$1')}: ${visibility(other.probe)}`).join('; ') || '-';
    line(`| ${name} | ${cycle.cycle} | ${cycle.launch.ms}${cycle.launch.isError ? ' (error)' : ''} | ${urlsOf(cycle.launch.tabs)} | ${reopened ? reopened.afterMs : '-'} | ${urlsOf(last)} | ${recoveryOf(cycle.selectedBeforeTheTidy)} | ${visibility(cycle.currentBeforeTheTidy)} | ${tidy} | ${visibility(cycle.currentAfterTheTidy)} | ${cycle.snapshot.isError ? 'error' : 'answered'} | ${others} | ${savedOf(cycle.saved)} |`);
  }
  if (result.outcome !== 'measured') line(`| ${name} | - | - | ${result.outcome} | | | | | | | | | |`);
}

line();
line('| Repetition | Resume | One read of every tab, ms | Tabs | ms per tab | Document URIs | One tab at a time, ms per tab |');
line('|---|--:|--:|--:|--:|---|---|');
for (const name of repetitions) {
  const result = await read(join(batch, name, 'result.json'));
  for (const cycle of result?.cycles ?? []) {
    const all = cycle.documentRead;
    if (!all) continue;
    const uris = (all.pages ?? []).map((page) => `${page.url === 'about:blank' ? 'blank' : page.url.replace(/^http:\/\/127\.0\.0\.1:(\d+)\/[^?]*/, ':$1')}=${String(page.documentURI).slice(0, 28)}`).join(', ') || `unread: ${(all.text ?? '').slice(0, 60)}`;
    const oneByOne = (cycle.others ?? []).map((other) => other.msForThisTab).join(', ') || '-';
    line(`| ${name} | ${cycle.cycle} | ${all.ms}${all.isError ? ' (error)' : ''} | ${all.tabs} | ${all.msPerTab ?? '-'} | ${uris} | ${oneByOne} |`);
  }
}

line();
line('| Arm | Repetitions | Launches past 5 s | First list the blank page alone | Tidy failed | Snapshot failed | Second resume met the first one\'s tabs |');
line('|---|--:|--:|--:|--:|--:|--:|');
for (const [arm, results] of byArm) {
  const cycles = results.flatMap((result) => result.cycles ?? []);
  const same = results.filter((result) => {
    const [one, two] = result.cycles ?? [];
    if (!one || !two) return false;
    const finalOf = (cycle) => (cycle.finalTabs ?? []).map((tab) => tab.url).sort().join(' ');
    return finalOf(one) === finalOf(two);
  }).length;
  line(`| ${arm} | ${results.length} | ${cycles.filter((cycle) => cycle.launch.ms > 5000).length} | ${cycles.filter((cycle) => cycle.launch.tabs.length === 1 && cycle.launch.tabs[0].url === 'about:blank').length} of ${cycles.length} | ${cycles.filter((cycle) => cycle.tidy && (cycle.tidy.select.isError || cycle.tidy.close.isError)).length} | ${cycles.filter((cycle) => cycle.snapshot.isError).length} | ${same} of ${results.length} |`);
}

process.stdout.write(`${out.join('\n')}\n`);
