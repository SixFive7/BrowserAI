// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Seeds a foreign 'browserai' entry into the sandbox ~/.claude.json, as another install would have written it.
const fs = require("fs");
const f = process.argv[2];
const j = JSON.parse(fs.readFileSync(f, "utf8"));
j.mcpServers.browserai = { type: "stdio", command: String.raw`D:\Other\BrowserAI.app\current\BrowserAI.Server.exe`, args: ["--my-own-flag"], env: { MINE: "1" } };
fs.writeFileSync(f, JSON.stringify(j, null, 2));
console.log("seeded foreign:", JSON.stringify(j.mcpServers.browserai));
