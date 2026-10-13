// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// node cut.mjs <evidence folder> <batch folder> ...: cuts what the rig wrote into an
// evidence batch, one subfolder per batch. A file whose bytes change in any way is written
// under a '.trimmed.' name, and every original read is listed in originals.sha256 with its
// SHA-256 and size; cuts.txt says what was done to each. The changes are three: the user's
// profile path becomes %USERPROFILE%, terminal colour escapes go, and a file too large to
// keep is cut down to the lines a finding rests on, by the rule named in cuts.txt.
// Profiles, snapshots and browser binaries are never copied.

import { createHash } from 'node:crypto';
import { mkdir, readdir, readFile, stat, writeFile } from 'node:fs/promises';
import { basename, dirname, extname, join, relative } from 'node:path';

const KEEP_WHOLE_BYTES = 1024 * 1024;
const LINE_CAP = 4000;
const MOZ_LINE_CAP = 20000;

const [evidence, ...batches] = process.argv.slice(2);
if (!evidence || batches.length === 0) throw new Error('usage: node cut.mjs <evidence folder> <batch folder> ...');

const profile = process.env.USERPROFILE;
if (!profile) throw new Error('USERPROFILE is not set, so the profile path cannot be replaced');
const escapeRegExp = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const spellings = [
  profile.replace(/\\/g, '\\\\'),
  profile,
  profile.replace(/\\/g, '/'),
  `/${profile[0].toLowerCase()}${profile.slice(2).replace(/\\/g, '/')}`,
].map((spelling) => new RegExp(escapeRegExp(spelling), 'gi'));

const clean = (text) => {
  let out = text.replace(/\u001b\[[0-9;]*[A-Za-z]/g, '').replace(/\\u001b\[[0-9;]*[A-Za-z]/g, '');
  for (const spelling of spellings) out = out.replace(spelling, '%USERPROFILE%');
  return out;
};

const sha256 = (buffer) => createHash('sha256').update(buffer).digest('hex');

async function exists(path) {
  try { await stat(path); return true; } catch { return false; }
}

const trimmedName = (name) => {
  const extension = extname(name);
  return `${name.slice(0, name.length - extension.length)}.trimmed${extension}`;
};

async function cutBatch(batch) {
  const name = basename(batch);
  const target = join(evidence, name);
  const digests = [];
  const cuts = [];

  // Writes the cut of one original: unchanged bytes keep the name, changed ones do not. A
  // control character other than a tab or a line end is refused, since a tracked text file
  // may not carry one.
  const keep = async (relativePath, text, rule) => {
    const original = await readFile(join(batch, relativePath));
    digests.push(`${sha256(original)}  ${original.length}  ${relativePath.replace(/\\/g, '/')}`);
    const output = text ?? clean(original.toString('utf8'));
    const control = output.search(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/);
    if (control >= 0) throw new Error(`${relativePath} still holds a control character at ${control}: ${JSON.stringify(output.slice(Math.max(0, control - 40), control + 40))}`);
    const changed = output !== original.toString('utf8');
    const outPath = join(target, changed ? trimmedName(relativePath) : relativePath);
    await mkdir(dirname(outPath), { recursive: true });
    await writeFile(outPath, output);
    cuts.push(`${relative(target, outPath).replace(/\\/g, '/')}: ${changed ? rule ?? 'the profile path replaced and colour escapes removed' : 'unchanged'}`);
  };

  const keepIfThere = async (relativePath, rule) => {
    if (await exists(join(batch, relativePath))) await keep(relativePath, null, rule);
  };

  for (const file of ['build.txt', 'browsers.txt', 'launcher.log', 'summary.txt']) await keepIfThere(file);
  for (const file of (await readdir(batch)).filter((entry) => /^registry-(?!before).*\.txt$/.test(entry))) await keepIfThere(file);
  if (await exists(join(batch, 'registry-before.txt'))) {
    const original = await readFile(join(batch, 'registry-before.txt'));
    digests.push(`${sha256(original)}  ${original.length}  registry-before.txt`);
    cuts.push(`registry-before.txt: not kept, it lists every value Firefox keeps for every install on the machine; ${original.toString('utf8').split(/\r?\n/).filter(Boolean).length} values`);
  }

  // The measure step.
  const measure = join(batch, 'measure');
  if (await exists(measure)) {
    const rows = [];
    try {
      for (const line of (await readFile(join(measure, 'results.jsonl'), 'utf8')).split(/\r?\n/).filter(Boolean)) rows.push(JSON.parse(line));
    } catch { /* a measure step that wrote no results */ }
    for (const file of ['results.jsonl', 'warmup.jsonl', 'events.jsonl', 'relay.stderr.txt', 'background.stdout.txt', 'node.stdout.txt', 'node.stderr.txt']) {
      await keepIfThere(join('measure', file));
    }

    // The background's records: whole when small, else the records of stalled sessions and
    // every warning or failure. A record starts at a line with the console's level prefix.
    const stderrPath = join('measure', 'background.stderr.txt');
    if (await exists(join(batch, stderrPath))) {
      const text = (await readFile(join(batch, stderrPath))).toString('utf8');
      if (Buffer.byteLength(text) <= KEEP_WHOLE_BYTES) {
        await keep(stderrPath);
      } else {
        const stalled = rows.filter((row) => row.firstStalled || row.secondStalled).map((row) => row.dir.toLowerCase());
        const records = [];
        for (const line of text.split(/\r?\n/)) {
          if (/^(trce|dbug|info|warn|fail|crit): /.test(line) || records.length === 0) records.push([line]);
          else records[records.length - 1].push(line);
        }
        const kept = records.filter((record) => /^(warn|fail|crit): /.test(record[0]) || stalled.some((dir) => record.join('\n').toLowerCase().includes(dir)));
        await keep(stderrPath, clean(kept.map((record) => record.join('\n')).join('\n')),
          `the records of the sessions whose start stalled and every warning or failure, ${kept.length} of ${records.length} records; the profile path replaced`);
      }
    }

    // The census: whole when small, else the snapshots inside each stalled call's window.
    const censusPath = join('measure', 'census.jsonl');
    if (await exists(join(batch, censusPath))) {
      const text = (await readFile(join(batch, censusPath))).toString('utf8');
      if (Buffer.byteLength(text) <= 2 * KEEP_WHOLE_BYTES) {
        await keep(censusPath);
      } else {
        const windows = rows.filter((row) => row.firstStalled).map((row) => [Date.parse(row.first.startedAt) - 10000, Date.parse(row.first.answeredAt) + 10000]);
        const lines = text.split(/\r?\n/).filter(Boolean);
        const inside = lines.filter((line) => {
          const at = Date.parse(JSON.parse(line).t.replace(/(\.\d{3})\d+/, '$1'));
          return windows.some(([from, to]) => at >= from && at <= to);
        });
        // A process is described once, on its first line, so the descriptions come along.
        const described = new Map();
        for (const line of lines) for (const member of JSON.parse(line).procs) if (member.exe !== undefined) described.set(`${member.pid}@${member.created}`, member);
        const needed = new Set(inside.flatMap((line) => JSON.parse(line).procs.map((member) => `${member.pid}@${member.created}`)));
        const descriptions = [...needed].filter((key) => described.has(key)).map((key) => JSON.stringify({ described: described.get(key) }));
        await keep(censusPath, clean([...descriptions, ...inside].join('\n') + '\n'),
          `the snapshots within 10 s of each stalled first call, ${inside.length} of ${lines.length}, and the description of each process in them; the profile path replaced`);
      }
    }

    // The configs BrowserAI wrote for each resumed child.
    const configs = join(measure, 'configs');
    if (await exists(configs)) {
      for (const file of (await readdir(configs)).filter((entry) => /^[a-z-]+-\d+\.json$/.test(entry))) await keepIfThere(join('measure', 'configs', file));
    }
  }

  // The replays.
  for (const folder of (await readdir(batch)).filter((entry) => entry.startsWith('replay-')).sort()) {
    for (const file of ['result.json', 'config.json', 'events.jsonl', 'node.stdout.txt', 'node.stderr.txt']) await keepIfThere(join(folder, file));

    const childPath = join(folder, 'child.stderr.txt');
    if (await exists(join(batch, childPath))) {
      const text = (await readFile(join(batch, childPath))).toString('utf8');
      if (Buffer.byteLength(text) <= 2 * KEEP_WHOLE_BYTES) {
        await keep(childPath);
      } else {
        const lines = text.split(/\r?\n/);
        const kept = lines.filter((line) => / pw:(protocol|browser|api) /.test(line)).map((line) => (line.length > LINE_CAP ? `${line.slice(0, LINE_CAP)} [cut at ${LINE_CAP} of ${line.length} characters]` : line));
        await keep(childPath, clean(kept.join('\n') + '\n'), `the pw:protocol, pw:browser and pw:api lines, ${kept.length} of ${lines.length}, each cut at ${LINE_CAP} characters; the profile path replaced`);
      }
    }

    // Firefox's own logs: the lines that name the page's port or an error page.
    const moz = join(batch, folder, 'moz');
    if (await exists(moz)) {
      let port = null;
      try { port = String(JSON.parse(await readFile(join(batch, folder, 'result.json'), 'utf8')).port); } catch { /* no result */ }
      for (const file of await readdir(moz)) {
        const text = (await readFile(join(moz, file))).toString('utf8');
        if (Buffer.byteLength(text) <= KEEP_WHOLE_BYTES) {
          await keep(join(folder, 'moz', file));
          continue;
        }
        const lines = text.split(/\r?\n/);
        const kept = lines.filter((line) => (port && line.includes(port)) || /neterror|about:blank|STATE_STOP|STATE_START|ProcessIsolation|BrowsingContext/.test(line)).slice(0, MOZ_LINE_CAP);
        await keep(join(folder, 'moz', file), clean(kept.join('\n') + '\n'),
          `the lines naming port ${port} or an error page, a document's start or stop, a process switch or a browsing context, ${kept.length} of ${lines.length}, at most ${MOZ_LINE_CAP}; the profile path replaced`);
      }
    }

    // Firefox's session store logs, written into the copy of the profile.
    const logs = join(batch, folder, 'profile', 'sessionstore-logs');
    if (await exists(logs)) {
      for (const file of await readdir(logs)) await keepIfThere(join(folder, 'profile', 'sessionstore-logs', file));
    }
  }

  await mkdir(target, { recursive: true });
  await writeFile(join(target, 'originals.sha256'), `${digests.join('\n')}\n`);
  await writeFile(join(target, 'cuts.txt'), `${cuts.join('\n')}\n`);
  process.stdout.write(`${name}: ${digests.length} originals, ${cuts.length} files written under ${target}\n`);
}

for (const batch of batches) await cutBatch(batch);
