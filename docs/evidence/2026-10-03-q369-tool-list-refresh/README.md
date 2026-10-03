<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 - Q369: what a client does with a tool list that changes, cut for the four records it corrected

**What this is.** The part of the Q369 measurement that four records now cite.
Q369 asked whether a placeholder tool list during an update, followed by
`notifications/tools/list_changed`, can carry a client across the update. It was
measured 2026-10-03 between 13:25Z and 14:12Z against a stand-in MCP server and
local API stubs, never against BrowserAI, at **Claude Code 2.1.288** (the CLI, and
byte for byte the binary the VS Code extension updated itself to that day),
**2.1.287** (the extension's binary of the morning), and **codex-cli
0.155.0-alpha.9.2** and **0.160.0**. Every client ran with its configuration in a
scratch folder. 165 counted runs; `REPORT.txt` is the measurer's own report and
`INDEX.txt` its map of the rig and the batches.

**What is kept is what the four corrections rest on**, run by run, and the tables
for the rest:

1. The terminal UI never starts a dead stdio server again: `runs/tui/TB-*`,
   `runs/tui2/TBclean-*` and `runs/tuiK/TBr-*`, nine runs, the server ending by
   `TerminateProcess` with exit code 1, by a clean exit 0, and holding the real
   tool list.
2. The same fact read against the claim that Claude Code re-launches a dead server
   transparently, which holds for `claude -p` and the stream-json transport: their
   counts are in `runs/summary-by-arm.tsv` (`PB`, `PBd`, `SB`, `SBts`, `PBr`,
   `SBr`).
3. A server that does not declare `capabilities.tools.listChanged` has its
   notification ignored: `runs/ccP/PA0-*` and `runs/ccS/SA0-*`, nine runs, through
   `claude -p` and through the stream-json transport with both binaries.
4. Tool search is off under a custom `ANTHROPIC_BASE_URL`: the first line of
   every `claude-debug.excerpt.txt` here, and one run with it forced on,
   `runs/ccS2/SAts-cli-r1`, for what the model is shown instead.

## Cited by

| Record | What it takes from here |
|---|---|
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The correction to the row on a server an update's kill pass ends before its handshake, and the one to the row Q261 closed |
| [kb: protocol](../../../kb/mcp/protocol.md) | The narrowing of the list-changed handler, the correction of the transparent re-launch, and the tool-search caveat on the stub measurements |
| [kb: re-verification](../../../kb/re-verification.md) | Rows 25, 149, 153 and 173 |
| [`DECISIONS.md`](../../../DECISIONS.md#the-update-lane-the-sessions-that-hold-it-and-the-second-client) | The correction of the Q286 b row's sentence that Claude Code starts the server again on its next call |
| `SessionErrors`, `ErrorCatalogueTests` | The Claude Code remedies of the two update refusals, which say what a terminal session needs since that day, and the arm that holds them |

## What is here

| Path | What it holds |
|---|---|
| `REPORT.txt`, `INDEX.txt` | The measurer's report and map, whole |
| `runs/summary-by-arm.tsv`, `runs/<batch>/table.tsv` | Every counted run, by arm and by batch |
| `runs/model-texts.txt` | What the model was handed, byte for byte, including with tool search on |
| `runs/<batch>/<run>/` | For each of the nineteen runs above: the stand-in's launch log and every frame it saw (`ph.launches.log`, `ph.wire.jsonl`), the harness log and result, what the scripted model was offered and did (`model.seen.jsonl`), how the server was registered (`mcp.json`), the rendered terminal screens for the terminal runs (`screens.txt`), and `claude-debug.excerpt.txt` |
| `rig/` | The stand-in server, the scripted Anthropic model, the two Claude Code batch generators and the summarising scripts as they ran, the two files of the exit harness the lane changed, and the script that cut this batch |
| `isolation/` | The client binaries' SHA-256, and the real Claude Code and Codex configuration hashed before and after |
| `originals.sha256`, `left-out.sha256` | What was changed and what was left out, each with its digest |

**The exit harness** is the probe record at
[`docs/probes/2026-10-03-client-exit`](../../probes/2026-10-03-client-exit/README.md)
with two files changed: `Harness.cs` gained the `touch`, `rm`, `waitFile` and
`snap` steps and `Pty.cs` a raw pseudoconsole capture. Those two are here whole;
the other five are identical to the probe record's apart from its header.

## What was cut, and what was left out

- **`claude-debug.excerpt.txt` is a selection, not a file the client wrote.** It
  is the lines of that run's `claude-debug.log` matching the pattern its own first
  lines state, in file order. The whole logs are left out, each with its digest
  in `left-out.sha256`, because they also carry the machine name and a great deal
  of unrelated client chatter.
- **Left out whole, with digests in `left-out.sha256`**: each run's
  `client-stdout.log`, `client-stderr.log`, `model.events.log`,
  `model.requests.jsonl` (the full request bodies, 170 to 700 KB a run, whose
  content `model.seen.jsonl` and `runs/model-texts.txt` digest), `pty-raw.bin`
  (the raw pseudoconsole stream, terminal escapes throughout), and the small
  marker files; and `code/offsets-claude.tsv`, which pairs each byte offset with
  a slice of the client's own code. The offsets themselves are in `REPORT.txt`.
- **Left out by directory**: each run's scratch client configuration, 4,948 files
  and 128,147,864 bytes over the nineteen runs, counted per run at the end of
  `left-out.sha256`; the client binaries, 1.3 GB of the scratch, with their
  digests in `isolation/`; the runs of every other arm; the shake-down batches; the slices
  of the Claude Code bundle and the `openai/codex` checkouts that were read.
- **Changed, with the original's digest in `originals.sha256`**: the
  user-profile path and the user name in the two `isolation/` files, stored under
  `.trimmed.` names, and the two-line SPDX header added to every rig file of a
  kind the header rule covers.
- **One rig file is stored under another name**: the terminal batch generator is
  `rig/gen-tui.js.txt`, because its line 103 calls `SCEN[sc](run)`, which the
  repository's link scan reads in any `.js` file as a Markdown link to a file
  named `run`. Its bytes are unchanged; its digest is in `originals.sha256`.

## Privacy

Nothing here names the user, the user profile or the machine. The API key the
Claude Code runs were given is the literal `stub-key-not-real`, and every model
request went to a stub on `127.0.0.1`.
