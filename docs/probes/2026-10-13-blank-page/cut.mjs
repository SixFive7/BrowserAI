// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// node cut.mjs <evidence folder> <batch folder> ...: cuts what this rig wrote into an
// evidence batch, one subfolder per batch, the way the restore-stall rig's cut.mjs does. A
// file whose bytes change is written under a '.trimmed.' name, and every original read is
// listed in originals.sha256 with its SHA-256 and size; cuts.txt says what was done to each.
// The changes are three: the user's profile path becomes %USERPROFILE%, terminal colour
// escapes go, and a child's standard error, which carries Playwright's whole protocol log, is
// cut to the lines that say which page attached when and what each page's main frame did.
// Profiles are never copied.

import { createHash } from 'node:crypto';
import { mkdir, readdir, readFile, stat, writeFile } from 'node:fs/promises';
import { basename, dirname, extname, join, relative } from 'node:path';

const LINE_CAP = 1500;

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
const exists = async (path) => { try { await stat(path); return true; } catch { return false; } };
const trimmedName = (name) => {
  const extension = extname(name);
  return `${name.slice(0, name.length - extension.length)}.trimmed${extension}`;
};

// The protocol lines a finding rests on: each page's attach, each main-frame navigation's
// commit, abort and readiness, its DOMContentLoaded and load, the launch line, and the API
// calls that bracket them.
const PROTOCOL_LINE = /Browser\.attachedToTarget|Browser\.detachedFromTarget|Page\.navigationCommitted|Page\.navigationAborted|Page\.ready|Page\.eventFired|<launching>|<launched>| pw:api /;

async function cutBatch(batch) {
  const name = basename(batch);
  const target = join(evidence, name);
  const digests = [];
  const cuts = [];

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

  for (const folder of (await readdir(batch, { withFileTypes: true })).filter((entry) => entry.isDirectory()).map((entry) => entry.name).sort()) {
    if (folder === 'tmp') {
      cuts.push('tmp: not kept, the profile issue\\restore.mjs made under os.tmpdir() and the browsers\' own temporary files');
      continue;
    }
    for (const file of ['result.json', 'config.json', 'events.jsonl', 'census.jsonl', 'node.stdout.txt', 'node.stderr.txt', 'repro.mjs', 'restore.mjs']) await keepIfThere(join(folder, file));
    if (await exists(join(batch, folder, 'node_modules'))) cuts.push(`${folder}/node_modules: not kept, the package.json and the one line of index.mjs that run.ps1 writes to re-export the payload's playwright-core`);
    for (const file of (await readdir(join(batch, folder))).filter((entry) => entry.endsWith('.stderr.txt') && entry !== 'node.stderr.txt')) {
      const text = (await readFile(join(batch, folder, file))).toString('utf8');
      const lines = text.split(/\r?\n/);
      const kept = lines.filter((line) => PROTOCOL_LINE.test(line)).map((line) => (line.length > LINE_CAP ? `${line.slice(0, LINE_CAP)} [cut at ${LINE_CAP} of ${line.length} characters]` : line));
      await keep(join(folder, file), clean(`${kept.join('\n')}\n`),
        `the lines of each page's attach, its main frame's commits, aborts, readiness, DOMContentLoaded and load, the launch and the API calls, ${kept.length} of ${lines.length}, each cut at ${LINE_CAP} characters; the profile path replaced`);
    }
    if (await exists(join(batch, folder, 'profile'))) cuts.push(`${folder}/profile: not kept, a browser's whole state`);
  }

  await mkdir(target, { recursive: true });
  await writeFile(join(target, 'originals.sha256'), `${digests.join('\n')}\n`);
  await writeFile(join(target, 'cuts.txt'), `${cuts.join('\n')}\n`);
  process.stdout.write(`${name}: ${digests.length} originals, ${cuts.length} files written under ${target}\n`);
}

for (const batch of batches) await cutBatch(batch);
