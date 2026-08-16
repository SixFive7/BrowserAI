// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Regenerates the four upstream snapshots from the ASSEMBLED payload.
//
// Build-order step 4, and it is deliberately not written in C#: three of the
// four snapshots can only be produced by the child itself, and the fourth --
// the tool list -- is what a real MCP `tools/list` returns over stdio, not what
// a static read of the bundle claims. Running under the payload's own node.exe
// is what makes `resolvedFrom.node` the version that actually ships rather than
// whatever node happens to be on PATH; the script asserts that below.
//
// It writes files and compares nothing. Comparing is Update-UpstreamSnapshots.ps1's
// job, so that "what upstream says" and "what we committed" never come from the
// same code path.

import { spawn } from 'node:child_process';
import { mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { join, resolve } from 'node:path';

const require = createRequire(import.meta.url);

// ---------------------------------------------------------------------------
// Arguments
// ---------------------------------------------------------------------------

function argument(name) {
  const index = process.argv.indexOf(`--${name}`);
  if (index < 0 || index + 1 >= process.argv.length) {
    throw new Error(`Missing required argument --${name}.`);
  }
  return resolve(process.argv[index + 1]);
}

const payloadRoot = argument('payload');
const outputRoot = argument('out');
const scratchRoot = argument('scratch');

const nodeExe = join(payloadRoot, 'node', 'node.exe');
const mcpRoot = join(payloadRoot, 'mcp');
const modules = join(mcpRoot, 'node_modules');
const cli = join(modules, '@playwright', 'mcp', 'cli.js');
const configTypes = join(modules, '@playwright', 'mcp', 'config.d.ts');
const browsersJson = join(modules, 'playwright-core', 'browsers.json');
const coreBundle = join(modules, 'playwright-core', 'lib', 'coreBundle.js');

// The whole point of the provenance stamp is that it names the runtime that
// produced the answer. A generator run under some other node would record the
// payload's version while measuring a different one, which is the exact shape
// of failure this repository exists to eliminate.
if (process.execPath.toLowerCase() !== nodeExe.toLowerCase()) {
  throw new Error(
    `This script must run under the payload's own node.exe.\n` +
    `  running under: ${process.execPath}\n` +
    `  payload node : ${nodeExe}`);
}

// ---------------------------------------------------------------------------
// The child's environment
// ---------------------------------------------------------------------------

// PLAYWRIGHT_MCP_CAPS alone would change the measured tool surface, and 42
// PLAYWRIGHT_MCP_* variables map onto config keys (kb/playwright/configuration.md).
// A snapshot that silently depends on the operator's environment is worse than
// no snapshot: it diffs when nothing upstream moved, and agrees when something
// did. Stripped rather than trusted, and the names are logged so a developer
// whose shell would have changed the answer finds out.
const childEnvironment = {};
const stripped = [];
for (const [name, value] of Object.entries(process.env)) {
  if (/^(PLAYWRIGHT|DEBUG|NODE_OPTIONS|NODE_PATH)/i.test(name)) {
    stripped.push(name);
    continue;
  }
  childEnvironment[name] = value;
}
if (stripped.length > 0) {
  console.error(`Stripped from the child environment: ${stripped.sort().join(', ')}`);
}

// ---------------------------------------------------------------------------
// One MCP session over stdio, hand-rolled
// ---------------------------------------------------------------------------

/**
 * Spawns cli.js, performs the handshake, asks for tools/list, and closes stdin.
 * No SDK: at step 4 there is no transport of ours yet, and a snapshot taken
 * through our own code would record whatever our code happened to accept.
 */
function session(label, config, protocolVersion) {
  return new Promise((succeed, fail) => {
    const directory = join(scratchRoot, label);
    mkdirSync(directory, { recursive: true });
    const configPath = join(directory, 'config.json');
    writeFileSync(configPath, JSON.stringify(config, null, 2), 'utf8');

    const child = spawn(nodeExe, [cli, '--config', configPath], {
      cwd: directory,
      env: childEnvironment,
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true,
    });

    const responses = new Map();
    let pending = '';
    let stderr = '';
    let settled = false;

    // Nothing here may outlive the script. A child holding the pipe open is
    // how a build hangs past its own timeout.
    const guard = setTimeout(() => {
      child.kill();
      finish(new Error(`${label}: no tools/list within 60s. stderr: ${stderr}`));
    }, 60_000);

    function finish(error, value) {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(guard);
      if (error) {
        fail(error);
      } else {
        succeed(value);
      }
    }

    const send = (message) => child.stdin.write(`${JSON.stringify(message)}\n`);

    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stderr.on('data', (chunk) => (stderr += chunk));
    child.on('error', (error) => finish(error));

    child.stdout.on('data', (chunk) => {
      pending += chunk;
      for (let end = pending.indexOf('\n'); end >= 0; end = pending.indexOf('\n')) {
        const line = pending.slice(0, end).trim();
        pending = pending.slice(end + 1);
        if (line.length === 0) {
          continue;
        }

        const message = JSON.parse(line);
        if (message.error) {
          child.kill();
          finish(new Error(`${label}: ${JSON.stringify(message.error)}`));
          return;
        }
        if (message.id === undefined) {
          continue;
        }

        responses.set(message.id, message.result);
        if (message.id === 1) {
          send({ jsonrpc: '2.0', method: 'notifications/initialized' });
          send({ jsonrpc: '2.0', id: 2, method: 'tools/list', params: {} });
        } else if (message.id === 2) {
          // Closing stdin is upstream's own shutdown path (setupExitWatchdog),
          // not a kill.
          child.stdin.end();
        }
      }
    });

    child.on('exit', (code) => {
      if (!responses.has(2)) {
        finish(new Error(`${label}: exited ${code} before tools/list. stderr: ${stderr}`));
        return;
      }
      finish(undefined, {
        initialize: responses.get(1),
        tools: responses.get(2).tools,
        stderr,
        exitCode: code,
      });
    });

    send({
      jsonrpc: '2.0',
      id: 1,
      method: 'initialize',
      params: {
        protocolVersion,
        capabilities: {},
        clientInfo: { name: 'browserai-upstream-snapshot', version: '1' },
      },
    });
  });
}

/** Captures a CLI invocation's stdout verbatim. */
function capture(args) {
  return new Promise((succeed, fail) => {
    // stdout is a pipe, never a terminal, which is what makes the help text
    // deterministic: commander wraps at process.stdout.columns and falls back
    // to 80 when that is undefined. Inheriting a terminal here would make the
    // snapshot depend on the window it was generated in.
    const child = spawn(nodeExe, [cli, ...args], {
      cwd: scratchRoot,
      env: childEnvironment,
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    });

    const chunks = [];
    let stderr = '';
    child.stdout.on('data', (chunk) => chunks.push(chunk));
    child.stderr.setEncoding('utf8');
    child.stderr.on('data', (chunk) => (stderr += chunk));
    child.on('error', fail);
    child.on('exit', (code) => code === 0
      ? succeed(Buffer.concat(chunks))
      : fail(new Error(`cli.js ${args.join(' ')} exited ${code}. stderr: ${stderr}`)));
  });
}

// ---------------------------------------------------------------------------
// Generate
// ---------------------------------------------------------------------------

rmSync(scratchRoot, { recursive: true, force: true });
mkdirSync(scratchRoot, { recursive: true });
mkdirSync(outputRoot, { recursive: true });

// Copied byte for byte, whatever the bytes are. Both are LF today (measured
// 2026-08-16); .gitattributes exempts this directory from normalisation so that
// the day either arrives CRLF, the gate reports a diff to adjudicate instead of
// git quietly rewriting one side of the comparison.
writeFileSync(join(outputRoot, 'config-schema.d.ts'), readFileSync(configTypes));
writeFileSync(join(outputRoot, 'browsers.json'), readFileSync(browsersJson));
writeFileSync(join(outputRoot, 'cli-help.txt'), await capture(['--help']));

const lock = JSON.parse(readFileSync(join(mcpRoot, 'package-lock.json'), 'utf8'));
const resolvedFrom = {
  '@playwright/mcp': lock.packages['node_modules/@playwright/mcp'].version,
  'playwright-core': lock.packages['node_modules/playwright-core'].version,
  node: process.version,
};

// The capability list comes out of the resolved config.d.ts, never out of this
// file: a literal here would silently stop covering a capability upstream adds,
// which is the one thing this snapshot exists to catch.
const union = /export type ToolCapability =([\s\S]*?);/.exec(readFileSync(configTypes, 'utf8'));
if (union === null) {
  throw new Error('config.d.ts no longer declares an "export type ToolCapability" union.');
}
const declaredCapabilities = [...union[1].matchAll(/'([^']+)'/g)].map((match) => match[1]);
if (declaredCapabilities.length === 0) {
  throw new Error('The ToolCapability union in config.d.ts parsed to zero capabilities.');
}

// The internal registry, read in-process. This is the only route to the tools
// that never reach `tools/list` at all, and to which capability each carries.
const { tools: bundle } = require(coreBundle);
const registry = bundle.browserTools.map((tool) => ({
  name: tool.schema.name,
  capability: tool.capability,
  skillOnly: tool.skillOnly === true,
}));

const toolsByCapability = {};
for (const capability of [...new Set(registry.map((tool) => tool.capability))].sort()) {
  toolsByCapability[capability] = registry
    .filter((tool) => tool.capability === capability)
    .map((tool) => tool.name);
}
const skillOnly = registry.filter((tool) => tool.skillOnly).map((tool) => tool.name);

const probedWith = '2999-01-01';
const ceilingProbe = await session('default', {}, probedWith);
const everything = await session('all-capabilities', { capabilities: declaredCapabilities }, probedWith);
const echoProbe = await session('echo', {}, '2025-06-18');

const exposed = everything.tools.map((tool) => tool.name);
const byDefault = ceilingProbe.tools.map((tool) => tool.name);

// Cross-checks between the wire and the bundle. These are the MECHANISM, not
// the numbers: the numbers are allowed to move and are recorded so that a move
// is a diff. If these two ever disagree, one of them is measuring something
// else and every count in this file is suspect.
const expectedExposed = registry.filter((tool) => !tool.skillOnly).map((tool) => tool.name);
const expectedDefault = expectedExposed
  .filter((name) => registry.find((tool) => tool.name === name).capability.startsWith('core'));

function assertSameSet(what, wire, expected) {
  const missing = expected.filter((name) => !wire.includes(name));
  const extra = wire.filter((name) => !expected.includes(name));
  if (missing.length > 0 || extra.length > 0) {
    throw new Error(
      `${what}: the wire and the internal registry disagree.\n` +
      `  only in the registry: ${missing.join(', ') || '(none)'}\n` +
      `  only on the wire    : ${extra.join(', ') || '(none)'}`);
  }
}

assertSameSet('the maximum exposed surface', exposed, expectedExposed);
assertSameSet('the default surface', byDefault, expectedDefault);

if (new Set(exposed).size !== exposed.length) {
  throw new Error('tools/list returned a duplicate tool name.');
}

const snapshot = {
  _what_this_is:
    'The golden tools/list snapshot, and the counts around it. Regenerated from the ' +
    'assembled payload and diffed on every build; a difference fails the build with the ' +
    'diff in the message. Generated by build/upstream-snapshots.mjs -- never edited by hand.',
  _regenerate: 'pwsh -File build/Update-UpstreamSnapshots.ps1 -Accept',
  _before_you_accept_a_change: 'UPSTREAM-REVIEW.md. Accepting a diff here is adopting an upstream version.',
  resolvedFrom,
  serverInfo: everything.initialize.serverInfo,
  serverCapabilities: everything.initialize.capabilities,
  protocol: {
    _how: 'Sent a deliberately-future revision and recorded what came back, then sent an ' +
      'older one. The child never rejects a version: it caps or echoes.',
    probedWith,
    ceiling: ceilingProbe.initialize.protocolVersion,
    echoed: { sent: '2025-06-18', negotiated: echoProbe.initialize.protocolVersion },
  },
  counts: {
    internalRegistry: registry.length,
    exposedMaximum: exposed.length,
    defaultSurface: byDefault.length,
    skillOnly: skillOnly.length,
  },
  declaredCapabilities,
  // A capability declared in config.d.ts that no tool carries does nothing when
  // set. Recorded rather than asserted: the day upstream gives one a tool, that
  // is a diff to adjudicate and not a build to fix.
  capabilitiesCarryingNoTool: declaredCapabilities.filter((capability) => !(capability in toolsByCapability)),
  // Every capability whose name starts with `core` is unconditional --
  // filteredTools() ors it with the configured list -- so these are on even
  // when `capabilities` names something else entirely.
  unconditionalCapabilities: Object.keys(toolsByCapability).filter((capability) => capability.startsWith('core')),
  toolsByCapability,
  skillOnly,
  defaultSurface: byDefault,
  tools: everything.tools,
};

// JSON.stringify's own line breaks are `\n` and it escapes any control
// character inside a string, so this is LF by construction on every platform.
writeFileSync(join(outputRoot, 'tools-list.json'), `${JSON.stringify(snapshot, null, 2)}\n`, 'utf8');

rmSync(scratchRoot, { recursive: true, force: true });

console.error(
  `tools/list: ${snapshot.counts.defaultSurface} default, ${snapshot.counts.exposedMaximum} exposed, ` +
  `${snapshot.counts.internalRegistry} internal, ${snapshot.counts.skillOnly} skill-only; ` +
  `protocol ceiling ${snapshot.protocol.ceiling}`);
