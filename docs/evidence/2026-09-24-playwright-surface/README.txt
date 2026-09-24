Raw dumps for the "Playwright features not exposed" review, 2026-09-24.
Read from payload/mcp/node_modules as assembled 2026-09-22
(@playwright/mcp 0.0.82, playwright-core 1.64.0-alpha-1789764292000, node v24.21.0).

decorateMCPCommand.txt        coreBundle.js:74985-75270 verbatim -- the MCP server command,
                              all 53 declared options and the factory/transport action.
mcp-options.json              the same 53 options enumerated THROUGH commander itself
                              (flags, attributeName, variadic, hidden), not by regex.
config-resolution.txt         coreBundle.js:73600-73990 -- resolveCLIConfigForMCP,
                              configFromCLIOptions (option -> config key), configFromEnv
                              (the PLAYWRIGHT_MCP_* variables), loadConfig, mergeConfig.
config-schema.d.ts            @playwright/mcp/config.d.ts verbatim -- the JSON config schema.
ini-longhand-keys.txt         coreBundle.js:73536-73625 -- the .ini flat-key table, which is
                              a WIDER surface than the CLI (launchOptions.slowMo,
                              contextOptions.bypassCSP, saveVideo, ...).
cli-client-help.txt           lib/tools/cli-client/help.json rendered -- global help plus
                              all 102 commands with args and flags.
cli-commands-source.txt       coreBundle.js:69694-70960 -- the command declarations, from
                              which the command -> toolName map was extracted.
tools-and-params.txt          all 72 wire tools with parameter names and descriptions,
                              from upstream-snapshots/tools-list.json.
capability-and-verdict-map.txt  all 83 registry tools x capability x on-the-wire x BrowserAI
                              verdict, plus the counts and which capabilities are unconditional.
options-vs-browserai.txt      per-option: what BrowserAI does with it today.
