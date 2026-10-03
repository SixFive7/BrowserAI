// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

const queries = ["mcp install","mcp config","mcp configuration","mcpServers","claude desktop","claude code","codex mcp","mcp client config","mcp registry","model context protocol configuration","mcp installer","tags:mcp","mcp settings","mcp setup","mcp manager","mcp json","claude_desktop_config","mcp.json","cursor mcp","vscode mcp"];
const seen = new Map();
for (const q of queries) {
  const url = `https://azuresearch-usnc.nuget.org/query?q=${encodeURIComponent(q)}&take=100&prerelease=true&semVerLevel=2.0.0`;
  try {
    const r = await fetch(url); const j = await r.json();
    for (const p of j.data) if (!seen.has(p.id)) seen.set(p.id, { ...p, q });
    console.error(q, j.totalHits);
  } catch (e) { console.error("FAIL", q, e.message); }
}
const all = [...seen.values()];
const rel = all.filter(p => /config|install|regist|claude|codex|cursor|vs ?code|windsurf|client setting|mcpServers|mcp\.json|settings/i.test((p.description||"") + " " + (p.title||"") + " " + p.id) && /mcp|model context/i.test((p.description||"") + " " + p.id + " " + (p.tags||[]).join(" ")));
rel.sort((a,b)=>b.totalDownloads-a.totalDownloads);
const fs = await import("fs");
fs.writeFileSync("nuget-candidates.json", JSON.stringify(rel.map(p=>({id:p.id,version:p.version,downloads:p.totalDownloads,authors:p.authors,verified:p.verified,description:p.description,projectUrl:p.projectUrl,q:p.q})),null,1));
console.log("unique packages:", all.length, "relevant by keyword:", rel.length);
for (const p of rel.slice(0,70)) console.log(`${p.id} ${p.version} dl=${p.totalDownloads} :: ${(p.description||"").replace(/\s+/g," ").slice(0,150)}`);
