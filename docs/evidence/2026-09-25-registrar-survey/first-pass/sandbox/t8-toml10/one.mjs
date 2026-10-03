// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

import { pathToFileURL } from "node:url";
const lib = await import(pathToFileURL(process.env.ADDMCP_LIB).href);
console.log(JSON.stringify(lib.upsertServer("codex", "browserai", { command: process.env.SERVER_W, args: [] })));
