// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Writes q304/hook-env.json: the three variables that keep the installer hooks inside
// scratch -- the suite's own sandbox (RealInstallerTests.cs:136-141).
'use strict';
const fs = require('fs');
const win = (p) => p.replace(/\//g, '\\');
const q = 'C:/Source/SixFive7/BrowserAI/.work/client-behaviour/q304';
const env = {
  CLAUDE_CONFIG_DIR: win(`${q}/hook-claude`),
  CODEX_HOME: win(`${q}/hook-codexhome`),
  BROWSERAI_ROOT: win('%USERPROFILE%/Downloads/tmp-client-behaviour/q304/data'),
};
fs.writeFileSync(`${q}/hook-env.json`, JSON.stringify(env, null, 1));
console.log(JSON.stringify(env));
