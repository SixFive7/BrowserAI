<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-24 -- everything `@playwright/mcp` offers, against what BrowserAI uses

**What this is.** The raw dumps behind the feature review: every option the MCP
server command declares, every key its config schema and its `.ini` table accept,
every tool in its registry with its capability, and the whole `cli-client`
command surface -- each set against what BrowserAI does with it today.
**12 files, 186,775 bytes.** Read out of `payload/mcp/node_modules` as assembled
2026-09-22: `@playwright/mcp` **0.0.82**, `playwright-core`
**1.64.0-alpha-1789764292000**, node **v24.21.0**.

**Every enumeration here was taken through the library itself and not by pattern.**
The 53 options came out of `commander`; the 83 registry tools came out of the
registry; the 72 wire tools came out of the golden `tools-list.json`. A regex over
a bundle would have produced a plausible list nobody could check.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#the-surface-browserai-does-not-use----read-2026-09-24) | The catalogue: what is set, what is set deliberately against upstream's default, what is refused on the environment route, and what is never written |
| [`TODO.md`](../../../TODO.md) | The five candidate follow-ups the maintainer may pick |

## What is here

| File | What it holds |
|---|---|
| [`README.txt`](README.txt) | The researcher's own note on where each dump came from, by file and line range |
| `decorateMCPCommand.txt` | `coreBundle.js:74985-75270` verbatim: the command, all 53 declared options, and the factory and transport action |
| `mcp-options.json` | The same 53 options enumerated **through `commander`** -- flags, attribute name, variadic, hidden |
| `config-resolution.txt` | `resolveCLIConfigForMCP`, `configFromCLIOptions`, `configFromEnv` and `mergeConfig`: the option-to-key map and the merge order |
| `config-schema.d.ts` | `@playwright/mcp/config.d.ts` verbatim |
| `ini-longhand-keys.txt` | The flat `.ini` key table, which is a **wider** surface than the command line |
| `cli-client-help.txt` | The `cli-client` help rendered: global help plus all 102 commands |
| `cli-commands-source.txt` | The command declarations those 102 came from, and the command-to-tool-name map |
| `tools-and-params.txt` | All 72 wire tools with their parameter names and descriptions |
| `capability-and-verdict-map.txt` | All 83 registry tools by capability and by BrowserAI verdict, with the counts and which capabilities are unconditional |
| `options-vs-browserai.txt` | **The answer, per option**: SET, DIFF, NOT SET or REFUSED, each with the file and line in this repository that decides it |

## What was cut

**Nothing.** These are text dumps of a bundled JavaScript tree and its type
definitions; the tree itself is the payload, which this repository never commits
and which [`build/payload/package-lock.json`](../../../build/payload/package-lock.json)
pins.
