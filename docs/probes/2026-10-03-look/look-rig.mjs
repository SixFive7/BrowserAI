// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Q317 c's three measurements, taken before the view-only look at a headless
// session is built: what a picture taken beside a running agent call does to that
// call, whether a picture written over one file leaves anything else behind, and
// what one picture costs.
//
//   node look-rig.mjs <BrowserAI.Server.exe> <scratch folder> <chromium|firefox> [rounds]
//
// It drives the published server over stdio as a client does, with a page server of
// its own on 127.0.0.1, and writes results-<family>.json into the scratch folder. The
// picture is browser_take_screenshot with a fixed file name, sent beside the agent's
// call on the same session, which is what the server would send to its own child.
//
// The first measurement has two halves. Time and errors: four agent calls, each
// alone and then with a picture taken a second into it. And what the agent's answer
// reports: every answer of the child carries what happened in the page since the
// last answer (a changed title or address, new console lines, a download), so a
// call that makes those happen is run alone and with a picture a second into it,
// and once more with the events arriving between two agent calls and the picture
// taken between them. Which answer reports them is recorded for each.
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { performance } from 'node:perf_hooks';

const [, , server, scratch, family, roundsText = '3'] = process.argv;
const rounds = Number(roundsText);

if (!server || !scratch || !family) {
  console.error('usage: node look-rig.mjs <BrowserAI.Server.exe> <scratch folder> <chromium|firefox> [rounds]');
  process.exit(2);
}

fs.mkdirSync(scratch, { recursive: true });

// A page with something to draw, one that answers four seconds late, and a file to download.
const rows = Array.from({ length: 60 }, (_, index) => `<div class="row">row ${index}: the quick brown fox jumps over the lazy dog, ${'x'.repeat(index % 17)}</div>`).join('');
const page = `<!doctype html><title>look rig</title><style>body{margin:0;font:16px system-ui}.row{height:40px;border-bottom:1px solid #ccc;padding:8px}.row:nth-child(odd){background:#eef}</style><h1>look rig</h1>${rows}`;
const site = http.createServer((request, response) => {
  if (request.url.startsWith('/slow')) {
    setTimeout(() => {
      response.writeHead(200, { 'content-type': 'text/html' });
      response.end('<!doctype html><title>slow</title><h1>slow page</h1>');
    }, 4000);
    return;
  }

  if (request.url.startsWith('/file')) {
    response.writeHead(200, { 'content-type': 'text/plain', 'content-disposition': 'attachment; filename="rig-download.txt"' });
    response.end('a file the rig downloads\n');
    return;
  }

  response.writeHead(200, { 'content-type': 'text/html' });
  response.end(page);
});

await new Promise((resolve) => site.listen(0, '127.0.0.1', resolve));

const base = `http://127.0.0.1:${site.address().port}`;
const child = spawn(server, [], { cwd: scratch, stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
const pending = new Map();
let buffered = '';
let nextId = 1;

child.stdout.setEncoding('utf8');
child.stdout.on('data', (chunk) => {
  buffered += chunk;

  for (let newline = buffered.indexOf('\n'); newline >= 0; newline = buffered.indexOf('\n')) {
    const line = buffered.slice(0, newline);
    buffered = buffered.slice(newline + 1);

    if (line.trim()) {
      const message = JSON.parse(line);

      if (message.id !== undefined && pending.has(message.id)) {
        pending.get(message.id)(message);
        pending.delete(message.id);
      }
    }
  }
});
child.stderr.on('data', (data) => fs.appendFileSync(path.join(scratch, `server-stderr-${family}.log`), data));

function request(method, params) {
  const id = nextId++;

  return new Promise((resolve) => {
    pending.set(id, resolve);
    child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
  });
}

const session = path.join(scratch, `look-${family}`);

// What an answer reports about the page, read off the child's own section headings
// and lines.
function reported(text) {
  return {
    sections: [...text.matchAll(/^### (.+)$/gm)].map((match) => match[1]),
    page: /^### Page$/m.test(text),
    console: /New console entries/.test(text),
    download: /Download(ing|ed) file/.test(text),
  };
}

async function call(name, args, why) {
  const started = performance.now();
  const answer = await request('tools/call', { name, arguments: { ...args, session, why } });
  const text = (answer.result?.content ?? []).filter((block) => block.type === 'text').map((block) => block.text).join('\n');

  return {
    tool: name,
    ms: Math.round(performance.now() - started),
    isError: Boolean(answer.result?.isError) || Boolean(answer.error),
    error: answer.error?.message ?? null,
    head: text.split('\n').slice(0, 3).join(' | '),
    url: /- Page URL: (\S+)/.exec(text)?.[1] ?? null,
    reported: reported(text),
    text,
  };
}

const look = () => call('browser_take_screenshot', { scale: 'css', type: 'png', filename: 'look.png' }, 'the Q317 c rig taking the picture a person asked for');

function listing(directory) {
  const found = [];
  const walk = (folder) => {
    for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
      const full = path.join(folder, entry.name);

      if (entry.isDirectory()) {
        // The browser's own profile changes on every call and is not the look's.
        if (entry.name !== 'profile') {
          walk(full);
        }
      } else {
        found.push({ file: path.relative(session, full), bytes: fs.statSync(full).size });
      }
    }
  };

  if (fs.existsSync(directory)) {
    walk(directory);
  }

  return found;
}

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

const initialize = await request('initialize', {
  protocolVersion: '2025-11-25',
  capabilities: {},
  clientInfo: { name: 'look-rig', version: '1' },
});

child.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' })}\n`);

// A client lists before it calls: a call that arrives first is refused once (Q261 b).
const listed = await request('tools/list', {});

const results = { family, base, started: new Date().toISOString(), serverInfo: initialize.result?.serverInfo ?? null, tools: listed.result?.tools?.length ?? null, cost: [], files: {}, beside: [], events: [] };

const opened = await request('tools/call', {
  name: 'browserai_init',
  arguments: { directory: session, purpose: 'the Q317 c measurement rig for the view-only look', browser: family },
});

results.init = { isError: Boolean(opened.result?.isError) };

await call('browser_navigate', { url: `${base}/` }, 'the rig putting a page with something to draw in front of the session');

results.userAgent = (await call('browser_evaluate', { function: '() => navigator.userAgent' }, 'the rig recording which browser build it measured')).text;

// Measurement 3: what one picture costs, and measurement 2: what twenty of them
// leave in the session's folder.
results.files.before = listing(session);

for (let index = 0; index < 22; index++) {
  const taken = await look();
  const written = listing(session).filter((entry) => path.basename(entry.file) === 'look.png');

  taken.bytes = written.length === 1 ? written[0].bytes : null;
  taken.where = written.map((entry) => entry.file);
  delete taken.text;

  if (index >= 2) {
    results.cost.push(taken);
  }
}

results.files.after = listing(session);

const slim = (entry) => {
  const { text, ...rest } = entry;
  return rest;
};

// Measurement 1, first half: an agent call with a picture taken a second into it,
// against the same call alone.
async function beside(label, agent) {
  for (let round = 0; round < rounds; round++) {
    const alone = await agent();
    const running = agent();

    await delay(1000);

    const picture = await look();
    const withPicture = await running;

    results.beside.push({ label, round, alone: slim(alone), withPicture: slim(withPicture), picture: slim(picture) });
  }
}

await call('browser_navigate', { url: `${base}/` }, 'the rig putting the drawing page back before the timed calls');
await beside('navigate to a page that answers in four seconds', () => call('browser_navigate', { url: `${base}/slow?${Math.random()}` }, 'the rig navigating the way an agent does'));
await call('browser_navigate', { url: `${base}/` }, 'the rig putting the drawing page back');
await beside('wait for three seconds', () => call('browser_wait_for', { time: 3 }, 'the rig waiting the way an agent does'));
await beside('evaluate a script that keeps the page busy for three seconds', () => call('browser_evaluate', { function: '() => { const end = Date.now() + 3000; while (Date.now() < end) {} return "done"; }' }, 'the rig running a script the way an agent does'));
await beside('an agent taking its own screenshot', () => call('browser_take_screenshot', { scale: 'css', type: 'png' }, 'the rig taking a screenshot the way an agent does'));

// Measurement 1, second half: what the agent's answers report when a picture is
// taken while events are waiting to be reported.
const makeEvents = (tag) => `() => {
  console.log('rig console line ${tag} ' + Math.random());
  document.title = 'retitled ${tag} ' + Math.random();
  const link = document.createElement('a');
  link.href = '/file?' + Math.random();
  link.download = 'rig-download.txt';
  document.body.append(link);
  link.click();
  link.remove();
}`;

const during = () => call(
  'browser_evaluate',
  { function: `async () => { (${makeEvents('during')})(); await new Promise((resolve) => setTimeout(resolve, 3000)); return 'done'; }` },
  'the rig running a call during which the page logs, retitles itself and downloads a file');

for (let round = 0; round < rounds; round++) {
  await call('browser_navigate', { url: `${base}/` }, 'the rig putting the drawing page back');
  const alone = await during();

  await call('browser_navigate', { url: `${base}/` }, 'the rig putting the drawing page back');
  const running = during();

  await delay(1000);

  const picture = await look();
  const withPicture = await running;

  results.events.push({ label: 'events during the agent call', round, alone: slim(alone), withPicture: slim(withPicture), picture: slim(picture) });
}

const schedule = () => call(
  'browser_evaluate',
  { function: `() => { setTimeout(${makeEvents('between')}, 500); return 'scheduled'; }` },
  'the rig scheduling events for after its call has answered');
const next = () => call('browser_evaluate', { function: '() => document.title' }, 'the rig making the next call an agent makes');

for (let round = 0; round < rounds; round++) {
  await call('browser_navigate', { url: `${base}/` }, 'the rig putting the drawing page back');
  await schedule();
  await delay(2500);
  const alone = await next();

  await call('browser_navigate', { url: `${base}/` }, 'the rig putting the drawing page back');
  await schedule();
  await delay(2500);
  const picture = await look();
  const withPicture = await next();

  results.events.push({ label: 'events between two agent calls', round, alone: slim(alone), withPicture: slim(withPicture), picture: slim(picture) });
}

results.files.end = listing(session);

await request('tools/call', { name: 'browserai_destroy', arguments: { directory: session, why: 'the Q317 c rig is done with its session' } });

results.finished = new Date().toISOString();
fs.writeFileSync(path.join(scratch, `results-${family}.json`), JSON.stringify(results, null, 2));

child.stdin.end();
site.close();

const costs = results.cost.map((entry) => entry.ms).sort((a, b) => a - b);
const bytes = results.cost.map((entry) => entry.bytes ?? 0).sort((a, b) => a - b);
const flags = (entry) => `page ${entry.reported.page ? 'yes' : 'no'}, console ${entry.reported.console ? 'yes' : 'no'}, download ${entry.reported.download ? 'yes' : 'no'}`;

console.log(`${family}: ${results.userAgent.split('\n').find((line) => line.includes('Mozilla')) ?? results.userAgent.slice(0, 200)}`);
console.log(`${family}: ${results.cost.length} pictures, ${costs[0]} to ${costs.at(-1)} ms (median ${costs[Math.floor(costs.length / 2)]}), ${bytes[0]} to ${bytes.at(-1)} bytes, errors ${results.cost.filter((entry) => entry.isError).length}`);
console.log(`${family}: files before ${results.files.before.length}, after twenty-two pictures ${results.files.after.length}, at the end ${results.files.end.length}`);

for (const entry of results.beside) {
  console.log(`${family}: ${entry.label} #${entry.round}: alone ${entry.alone.ms} ms ${entry.alone.isError ? 'ERROR' : 'ok'}, with a picture ${entry.withPicture.ms} ms ${entry.withPicture.isError ? 'ERROR' : 'ok'}, the picture ${entry.picture.ms} ms ${entry.picture.isError ? 'ERROR' : 'ok'}`);
}

for (const entry of results.events) {
  console.log(`${family}: ${entry.label} #${entry.round}: alone [${flags(entry.alone)}]; with a picture, the agent's answer [${flags(entry.withPicture)}] and the picture's [${flags(entry.picture)}]`);
}
