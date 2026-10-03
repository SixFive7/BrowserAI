// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr

// Starts the same @playwright/mcp 0.0.82 BrowserAI bundles, configured the way BrowserAI configures it
// (capabilities, console level, codegen none, absolute file paths, snapshot boxes, CfT channel, headless,
// persistent profile, 1920x1080), with every path under scratch. Scratch only.
import fs from "node:fs";
import path from "node:path";
import { McpClient } from "./mcpclient.mjs";
import { BENCH, scratchEnv } from "./lib.mjs";

export function writeConfig(tag, { boxes = true } = {}) {
  const dir = path.join(BENCH, "profiles", `pw-${tag}`);
  const outDir = path.join(BENCH, "out", `pw-${tag}-output`);
  fs.mkdirSync(outDir, { recursive: true });
  const cfg = {
    browser: {
      browserName: "chromium",
      userDataDir: dir,
      launchOptions: { channel: "chrome-for-testing", headless: true },
      contextOptions: { viewport: { width: 1920, height: 1080 } },
    },
    capabilities: ["config", "vision", "devtools", "storage", "network", "pdf", "testing"],
    outputDir: outDir,
    console: { level: "debug" },
    codegen: "none",
    filePaths: "absolute",
    snapshot: { boxes },
    allowUnrestrictedFileAccess: false,
  };
  const file = path.join(BENCH, "out", `pw-${tag}-config.json`);
  fs.writeFileSync(file, JSON.stringify(cfg, null, 1));
  return { file, outDir };
}

export async function startPwMcp(tag, opts) {
  const { file } = writeConfig(tag, opts);
  const cli = path.join(BENCH, "pw", "node_modules", "@playwright", "mcp", "cli.js");
  const client = new McpClient(process.execPath, [cli, "--config", file], scratchEnv(), BENCH);
  await client.initialize();
  return client;
}
