<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-24 -- Q261 against the real clients

**What this is.** Eleven runs of the real Claude Code CLI and the real Codex CLI
against the **published** BrowserAI, establishing what Q261's refusal actually
does at the other end: whether a client that re-dials a dead server meets it,
whether the sentence reaches the model, whether the retry it asks for works, and
whether the `notifications/tools/list_changed` sent with it changes anything.
**92 files, 878,686 bytes.** Taken at **Claude Code 2.1.281** and **codex-cli
0.155.0-alpha.9.2** on Windows 10.0.26200, against BrowserAI
**1.1.1-alpha.0.64** (CCQ2-CCQ6, CXQ1-CXQ3) and **1.1.1-alpha.0.65** (CCQ7-CCQ9,
after the wording these runs corrected), with a local API stub so no credential
is used and no inference happens anywhere.

⚠️ **Nothing here touched the maintainer's own state.** Claude Code ran under
`CLAUDE_CONFIG_DIR` at a scratch directory seeded with the client's own
throwaway-config recipe; Codex ran under `CODEX_HOME` at a scratch directory with
a `config.toml` the rig writes, and its CLI was taken from
`AppData\Local\OpenAI\Codex\bin`, which is outside `~\.codex` -- `~\.codex` was
hashed before and after every Codex run and is unchanged. Every BrowserAI server
ran with `BROWSERAI_ROOT` at a scratch app root, because the stray sweep is
machine-wide by design and the default root is shared with the installed
BrowserAI.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md#what-q261s-refusal-does-at-the-other-end----measured-2026-09-24) | Every number in *What Q261's refusal does at the other end* |
| [kb: re-verification](../../../kb/re-verification.md) | Row 153, keyed on both clients' versions |
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The frozen-tool-list row: what the refusal closes and what it does not |
| [`SessionErrors.ToolListPredatesThisServer`](../../../src/BrowserAI/Sessions/SessionErrors.cs) | The correction to the Claude Code remedy clause |
| [`ClientReconnectTests`](../../../tests/BrowserAI.Tests/ClientReconnectTests.cs) | The two arms that repeat the part a run can repeat |

## The runs

The rig is [`docs/probes/2026-09-24-q261`](../../probes/2026-09-24-q261/README.md).

| Run | Client | What it was for | What it found |
|---|---|---|---|
| `CCQ2` | Claude Code | The first full sequence: one call answered, the server ends, the re-dialled server refuses, the retry | 2 server processes; the refusal reached the model verbatim; the retry was answered |
| `CCQ3` | Claude Code | The same with 5.1 s of idle connection left AFTER the retry | No `tools/list` on the re-dialled connection |
| `CCQ4`-`CCQ6` | Claude Code | The same with the idle window moved BETWEEN the refusal and the retry, which is the window a refresh would have to land in | **3/3: no `tools/list` on the re-dialled connection at all** |
| `CCQ7`-`CCQ9` | Claude Code | The three runs against the corrected wording | 3/3 identical, byte for byte |
| `CXQ1`-`CXQ3` | Codex | Whether a Codex thread lists before it calls | **3/3: `initialize`, `notifications/initialized`, `tools/list`, then `tools/call`** -- the refusal never fires and no notification is ever sent |

## What is here

| Path | What it holds |
|---|---|
| `logs/<run>.wire.jsonl` | Every frame the shim saw, in order, with the shim's own pid -- which is how the number of server processes is counted and not assumed |
| `logs/<run>.launches.log` | One `LAUNCH` line per server process, and the line that ended one |
| `logs/<run>.requests.trimmed.jsonl` | The body of every API request the client sent, trimmed as described below. This is *exactly what the model saw* |
| `logs/<run>.server.stderr.log` | BrowserAI's own log for that run, which carries the refusal's `ProxyLog` line |
| `logs/CXQ*.driver.log` | The app-server JSON-RPC traffic, and Codex's own tracing at `codex_core::mcp=trace` |
| `out/<run>.stream.jsonl` | The client's `--output-format stream-json`, which is the turn as a human would have watched it |
| `out/CCQ3.debug.log`, `out/CCQ7.debug.log` | The client's own `--debug-file`, kept for one round per wording |
| `config/` | The two scratch client configurations the runs were pointed at |

## What was cut, and what it was cut from

⚠️ **Every request capture is trimmed and none is kept whole**, on the convention
[`2026-09-23-client-reconnect`](../2026-09-23-client-reconnect/README.md)
established. Each `*.requests.jsonl` is the body of every API request the client
sent, and most of each one is three things that are not evidence and one of which
is not ours to publish: the session's own system-reminder, the tool schemas of
whatever else was registered, and **the client's own system prompt**, which
belongs to a third party. All **8** are kept as `*.requests.trimmed.jsonl` with
three cuts, applied wherever they occur:

- the client's own `system` prompt, replaced by `[cut: the client's own system prompt]`;
- any text block containing `<system-reminder>`, replaced by `[cut: the session's system-reminder, which is this repository's own CLAUDE.md]`;
- every tool definition reduced to `{"name": ..., "cut": "description and schema"}`.

**Everything else is byte-for-byte**, including the turn number, the timestamp,
the URL, the message order and every `tool_result` -- which is the half the
findings turn on, because what is being established is the exact sentence the
model was handed.
[`logs/requests-originals.sha256`](logs/requests-originals.sha256) carries the
SHA-256 and the byte count of all **8** originals.

⚠️ **The client's own debug log is kept for two rounds and not eleven.** It is
45 KB a run and says the same thing in each; `CCQ3` is the round that established
the absent refresh and `CCQ7` the round against the corrected wording. The
repeats establish *3/3* and they establish it through the wire logs, which are
the thing being counted and which are all here.

⚠️ **Eleven empty files were dropped and not kept.** Three
`CXQ*.driver.out` and eight `CCQ*.stderr.txt` were zero bytes, which is itself a
finding -- the client wrote nothing to stderr in any run, and the app-server
driver wrote nothing to its own stdout -- and it is recorded here instead of as
eleven empty files.

⚠️ **Codex colours its tracing output, and the escape sequences are stripped.**
Three `CXQ*.driver.log` captures carried ANSI SGR sequences; what is stored is
what a terminal would have shown, with the escapes removed and nothing else
touched -- 23,638, 23,738 and 21,402 characters respectively. The tree refuses a
C0 control byte in text, and a captured terminal transcript is the one place they
arrive honestly.

**Line endings are this repository's**, per
[the directory's own note](../README.md): captures written with CRLF are stored
with LF.
