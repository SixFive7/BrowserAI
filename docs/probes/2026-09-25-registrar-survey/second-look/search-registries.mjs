// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Second-look registry search, 2026-10-01. Public search endpoints only (nuget.org, npmjs.org, crates.io).
// No GitHub API. Writes registry-candidates.json beside this file.
import fs from "node:fs";

const rel = (text) =>
  /mcp|model context/i.test(text) &&
  /install|regist|config|setup|claude|codex|cursor|client|agent|harness|host/i.test(text);

const out = { when: new Date().toISOString(), nuget: [], npm: [], crates: [] };

// ---- NuGet: queries the 2026-09-27 pass did not use
const nugetQueries = [
  "mcp register", "mcp registration", "AgentConfig", "agent config mcp", "mcp add", "mcp client registration",
  "coding agent mcp", "claude.json", "codex config.toml", "mcp host config", "agent harness", "mcp server installer",
  "mcp onboarding", "mcp deploy client", "McpPlugin", "mcp bundle", "mcpb",
];
{
  const seen = new Map();
  for (const q of nugetQueries) {
    try {
      const r = await fetch(`https://azuresearch-usnc.nuget.org/query?q=${encodeURIComponent(q)}&take=100&prerelease=true&semVerLevel=2.0.0`);
      const j = await r.json();
      for (const p of j.data) if (!seen.has(p.id)) seen.set(p.id, { ...p, q });
      console.error("nuget", q, j.totalHits);
    } catch (e) { console.error("nuget FAIL", q, e.message); }
  }
  const all = [...seen.values()];
  out.nugetUnique = all.length;
  out.nuget = all
    .filter((p) => rel(`${p.id} ${p.title || ""} ${p.description || ""} ${(p.tags || []).join(" ")}`))
    .sort((a, b) => b.totalDownloads - a.totalDownloads)
    .map((p) => ({ id: p.id, version: p.version, downloads: p.totalDownloads, authors: p.authors, description: (p.description || "").replace(/\s+/g, " ").slice(0, 300), projectUrl: p.projectUrl, q: p.q }));
}

// ---- npm
const npmQueries = [
  "add mcp server claude code codex", "mcp installer cli agents", "install mcp server clients", "mcp config writer",
  "mcp register client", "mcp setup claude cursor codex", "agent config mcp sync", "mcp client config library",
];
{
  const seen = new Map();
  for (const q of npmQueries) {
    try {
      const r = await fetch(`https://registry.npmjs.org/-/v1/search?text=${encodeURIComponent(q)}&size=60`);
      const j = await r.json();
      for (const o of j.objects) {
        const p = o.package;
        if (!seen.has(p.name)) seen.set(p.name, { name: p.name, version: p.version, date: p.date, description: (p.description || "").slice(0, 300), links: p.links?.repository || p.links?.npm, weekly: o.downloads?.weekly, monthly: o.downloads?.monthly, q });
      }
      console.error("npm", q, j.total);
    } catch (e) { console.error("npm FAIL", q, e.message); }
  }
  const all = [...seen.values()];
  out.npmUnique = all.length;
  out.npm = all.filter((p) => rel(`${p.name} ${p.description}`)).sort((a, b) => (b.monthly || 0) - (a.monthly || 0));
}

// ---- crates.io
const crateQueries = ["mcp install", "mcp config", "mcp client config", "mcp register", "mcp setup", "agent config mcp"];
{
  const seen = new Map();
  for (const q of crateQueries) {
    try {
      const r = await fetch(`https://crates.io/api/v1/crates?q=${encodeURIComponent(q)}&per_page=50`, { headers: { "User-Agent": "browserai-research (read-only survey)" } });
      const j = await r.json();
      for (const c of j.crates || []) if (!seen.has(c.id)) seen.set(c.id, { id: c.id, version: c.max_version, downloads: c.downloads, updated: c.updated_at, description: (c.description || "").replace(/\s+/g, " ").slice(0, 300), repository: c.repository, q });
      console.error("crates", q, j.meta?.total);
    } catch (e) { console.error("crates FAIL", q, e.message); }
  }
  const all = [...seen.values()];
  out.cratesUnique = all.length;
  out.crates = all.filter((p) => rel(`${p.id} ${p.description}`)).sort((a, b) => b.downloads - a.downloads);
}

fs.writeFileSync(new URL("./registry-candidates.json", import.meta.url), JSON.stringify(out, null, 1));
console.log(`nuget unique=${out.nugetUnique} relevant=${out.nuget.length}; npm unique=${out.npmUnique} relevant=${out.npm.length}; crates unique=${out.cratesUnique} relevant=${out.crates.length}`);
