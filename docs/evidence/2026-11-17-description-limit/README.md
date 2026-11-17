<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-11-17 -- where Claude Code 2.1.326 cuts a tool description and a server's instructions

**What this is.** Three captures, taken 2026-11-17 between 15:23Z and 15:24Z, of the
request Claude Code 2.1.326 sends to the API with a probe MCP server connected, by the
method of [`docs/probes/2026-08-18-truncation`](../../probes/2026-08-18-truncation/README.md):
that record's `capture.js` answered on `127.0.0.1` and wrote each request to disk with its
credential headers redacted, the client ran as `claude -p` with `ANTHROPIC_BASE_URL`
pointed at it and a throwaway `ANTHROPIC_AUTH_TOKEN`, so no model was called. The client
was a copy of `~\.local\bin\claude.exe`, SHA-256
`44e85ac6d3e36b924a0d631d3f69c13a6907491115d71e97c8eb6160c82b2aed`, with a scratch
`CLAUDE_CONFIG_DIR` whose `settings.json` refused this repository's four project servers,
`--strict-mcp-config` and an `--mcp-config` naming the probe alone, below-normal priority.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md#what-2kb-each-means----measured-2026-08-18--claude-code-21234) | The correction of 2026-11-17 |
| [kb: re-verification](../../../kb/re-verification.md) | Row 92 |

## The captures

| Folder | Model | Tool search | What arrived |
|---|---|---|---|
| `cap-01-sonnet` | `sonnet` | the client's default | 14 tools, `ToolSearch` among them, and none of the probe's; the probe's `instructions` cut at 4,096 |
| `cap-02-sonnet-no-tool-search` | `sonnet` | `ENABLE_TOOL_SEARCH=false` | All nine probe tools: descriptions of 2,047 to 4,096 characters whole, 4,097 and 5,000 cut to 4,096 and the 13-character suffix, the 20,000-character parameter description whole; `instructions` cut at 4,096 |
| `cap-03-haiku-no-tool-search` | `haiku` | `ENABLE_TOOL_SEARCH=false` | The same as `cap-02` |

Each folder holds `analysis.log.txt`, what `analyse4.js` printed; `body-001.trimmed.json`,
the captured request cut to the model, every tool's name, the probe's tools whole and the
stretch of message text holding the probe's `instructions`; `req-001.json`, the request
line and its headers as `capture.js` wrote them; the `mcp.json` the client was given, and
its own output.

## The rig

`rig/` holds `probe-server4.js`, which publishes each string at an exact length ending in
a marker naming that length and checks every length before it publishes; `run-trunc.ps1`,
which takes one capture; `analyse4.js`, which reads it; and `capture.js` as it ran, all
under `.txt` names.

## What was cut

Each request body is kept trimmed: the client's own system prompt and messages are not
here, and [`originals.sha256`](originals.sha256) gives the digest of each body as taken,
beside the digests of the files kept under `.txt` names. The scratch configurations the
client wrote are left out; [`left-out.sha256`](left-out.sha256) lists them. The trimmed
captures keep the client's suffix as it arrived, an ellipsis character among it. Line
endings are this repository's, LF.
