// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Pulls the byte-exact texts the model was handed out of the request captures, and the
// texts the hosts were handed out of the wire and driver logs, into one JSON file.
// usage: node texts.js <out.json>
'use strict';
const fs = require('fs');
const R = 'C:/Source/SixFive7/BrowserAI/.work/client-behaviour/runs';
const reqs = (p) => fs.readFileSync(p, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l));
const out = {};

// Claude Code, real server (CCR1), session 1: every text block that carries the MCP
// instructions, whole, and every tool_result the model was sent.
{
  const r = reqs(`${R}/CCR1/logs/CCR1-s1.requests.jsonl`);
  const last = r[r.length - 1].body;
  const blocks = [];
  for (const m of last.messages) {
    const parts = Array.isArray(m.content) ? m.content : [{ type: 'text', text: m.content }];
    for (const p of parts) {
      if (p.type === 'text' && /MCP Server Instructions/.test(p.text)) {
        const i = p.text.indexOf('<system-reminder>\n# MCP Server Instructions');
        const j = p.text.indexOf('</system-reminder>', i);
        blocks.push(p.text.slice(i, j + '</system-reminder>'.length));
      }
    }
  }
  out.claudeCode_real_s1 = {
    toolsOffered: (last.tools || []).map((t) => t.name),
    mcpInstructionsReminder: blocks,
    toolResults: last.messages.flatMap((m) => (Array.isArray(m.content) ? m.content : []).filter((c) => c.type === 'tool_result')),
    updateSentenceAnywhere: (JSON.stringify(last).match(/installing an update/g) || []).length,
  };
  const r2 = reqs(`${R}/CCR1/logs/CCR1-s2.requests.jsonl`);
  const l2 = r2[r2.length - 1].body;
  out.claudeCode_real_s2 = {
    toolsOffered: (l2.tools || []).length,
    lastToolResult: l2.messages.flatMap((m) => (Array.isArray(m.content) ? m.content : []).filter((c) => c.type === 'tool_result')).slice(-1),
  };
}

// Codex exec, real server (CXER1): the function_call_output items, whole.
for (const [key, file] of [['codexExec_real_s1', `${R}/CXER1/logs/CXER1-s1.requests.jsonl`], ['codexExec_real_s2', `${R}/CXER1/logs/CXER1-s2.requests.jsonl`], ['codexApp_real', `${R}/CXAR1/logs/CXAR1-model.requests.jsonl`]]) {
  const r = reqs(file);
  const last = r[r.length - 1].body;
  out[key] = {
    namespacesPerRequest: r.map((d) => (d.body.tools || []).filter((t) => t.type === 'namespace').map((t) => `${t.name}[${(t.tools || []).length}]`)),
    functionCallOutputs: (last.input || []).filter((i) => i.type === 'function_call_output'),
    updateSentenceAnywhere: r.map((d) => (JSON.stringify(d.body).match(/installing an update/g) || []).length),
  };
}

// The b-shaped runs: what the model was sent for a refused call and after the server went.
{
  const cc = reqs(`${R}/CCB1/logs/CCB1-s1.requests.jsonl`);
  out.claudeCode_bShape = cc[cc.length - 1].body.messages.flatMap((m) => (Array.isArray(m.content) ? m.content : []).filter((c) => c.type === 'tool_result'));
  const cx = reqs(`${R}/CXEB1/logs/CXEB1-s1.requests.jsonl`);
  out.codexExec_bShape = (cx[cx.length - 1].body.input || []).filter((i) => i.type === 'function_call_output');
}

// What the hosts were handed (app-server, real server): the startup status and the direct call's error.
{
  const lines = fs.readFileSync(`${R}/CXAR1/logs/CXAR1.driver.log`, 'utf8').split('\n');
  out.codexApp_real_host = {
    startupStatusFailed: lines.filter((l) => /startupStatus\/updated .*"failed"/.test(l)).map((l) => JSON.parse(l.replace(/^.*?startupStatus\/updated /, ''))),
    directCallErrors: lines.filter((l) => / RESP id=\d+ \{"code"/.test(l)).map((l) => JSON.parse(l.replace(/^.*? RESP id=\d+ /, ''))),
  };
}

// The error frame the real server sent for tools/list, per client, off the pass-through's wire log.
{
  const grab = (run) => fs.readFileSync(`${R}/${run}/logs/${run}.wire.jsonl`, 'utf8').split('\n').filter(Boolean).map((l) => JSON.parse(l)).filter((d) => d.frame && d.frame.error).map((d) => d.frame);
  out.realServer_toolsListError_claudeCode = grab('CCR1');
  out.realServer_toolsListError_codex = grab('CXER1').concat(grab('CXAR1'));
}

fs.writeFileSync(process.argv[2], JSON.stringify(out, null, 1));
console.log('written', process.argv[2]);
