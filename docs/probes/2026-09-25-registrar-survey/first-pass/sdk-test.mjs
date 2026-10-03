// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Drives add-mcp's programmatic API the way a registrar would, in whatever sandbox env is set.
import { pathToFileURL } from "node:url";
import { readFileSync, existsSync } from "node:fs";
const lib = await import(pathToFileURL(process.env.ADDMCP_LIB).href);
const server = process.env.SERVER_W, project = process.env.PROJECT_W;
const show = (label, r) => console.log(label.padEnd(34), JSON.stringify(r));
const step = process.argv[2];
if (step === "add") {
  show("upsert claude-code global", lib.upsertServer("claude-code", "browserai", { command: server, args: [] }));
  show("upsert codex global", lib.upsertServer("codex", "browserai", { command: server, args: [] }));
  show("upsert claude-code project", lib.upsertServer("claude-code", "browserai", { command: "${LOCALAPPDATA}/BrowserAI.app/current/BrowserAI.Server.exe", args: [] }, { local: true, cwd: project }));
  show("upsert codex project", lib.upsertServer("codex", "browserai", { command: "BrowserAI.Server.exe", args: [] }, { local: true, cwd: project }));
}
if (step === "list") {
  for (const global of [true, false]) {
    const all = await lib.listInstalledServers({ global, cwd: project, agents: ["claude-code", "codex"] });
    for (const a of all) console.log(global ? "global" : "project", a.agentType, "detected=" + a.detected, a.configPath, JSON.stringify(a.servers.map(s => [s.serverName, s.identity])), a.error ?? "");
  }
}
if (step === "remove") {
  show("remove claude-code global", lib.removeServer("claude-code", "browserai"));
  show("remove codex global", lib.removeServer("codex", "browserai"));
  show("remove claude-code project", lib.removeServer("claude-code", "browserai", { local: true, cwd: project }));
  show("remove codex project", lib.removeServer("codex", "browserai", { local: true, cwd: project }));
}
if (step === "detect") {
  console.log("detectGlobalAgents:", JSON.stringify(await lib.detectGlobalAgents?.()));
  console.log("detectProjectAgents:", JSON.stringify(lib.detectProjectAgents?.(project)));
}
