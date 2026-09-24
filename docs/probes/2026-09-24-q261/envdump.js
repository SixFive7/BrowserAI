// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// A stand-in MCP server that records the environment it was started with, then
// answers initialize and tools/list with nothing, so Codex sees a live server.
const fs = require('fs');
fs.writeFileSync(process.env.ENVDUMP_OUT || (__dirname + '/env-seen.json'), JSON.stringify(process.env, null, 1));
let buf = '';
process.stdin.on('data', (d) => {
  buf += d.toString('utf8');
  let nl;
  while ((nl = buf.indexOf('\n')) >= 0) {
    const line = buf.slice(0, nl).trim(); buf = buf.slice(nl + 1);
    if (!line) continue;
    let m; try { m = JSON.parse(line); } catch { continue; }
    if (m.method === 'initialize') process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: m.id, result: { protocolVersion: m.params.protocolVersion, capabilities: { tools: {} }, serverInfo: { name: 'envdump', version: '1' } } }) + '\n');
    else if (m.method === 'tools/list') process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: m.id, result: { tools: [] } }) + '\n');
    else if (m.id !== undefined) process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: m.id, result: {} }) + '\n');
  }
});
