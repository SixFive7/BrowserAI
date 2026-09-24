<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-23 / 2026-09-24 -- what each client does when a stdio MCP server exits

**What this is.** Three rounds of measurement behind Q254 and Q261: whether a
client re-launches a stdio MCP server that has exited, what the re-launched
server is sent, whether anything tells the model its tool surface moved, and
whether a relay or a handover can hold the transport open across an update.
**715 files, 4,546,018 bytes.** Taken at **Claude Code 2.1.281** and **codex-cli
0.155.0-alpha.9.2** on Windows 10.0.26200, against a purpose-built dummy MCP
server and a local API stub, with every client run pointed at a scratch
configuration directory.

⚠️ **Nothing here touched the maintainer's own client state.** Claude Code ran
under `CLAUDE_CONFIG_DIR` at `cfg/` and `cfg-user/`; Codex ran under `CODEX_HOME`
at `codexhome/`. Both are kept under `config/`, renamed with their directory as a
prefix so the two `.claude.json` files can sit side by side.
[`FINDINGS-INDEX.txt`](FINDINGS-INDEX.txt) is the researcher's own map of every
run and is the file to read first.

## Cited by

| Record | What it takes from here |
|---|---|
| [`DECISIONS.md`](../../../DECISIONS.md#the-update-lane-the-sessions-that-hold-it-and-the-second-client) | Q254 (no relay, and the relay as the direction not taken with its costs) and Q261 (one informed refusal plus the list-changed notification) |
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The row for a live session's frozen tool list |
| [kb: protocol](../../../kb/mcp/protocol.md) | Every measured number in *What a client does when the server exits* and *What the pipe decides* |
| [`RELEASING.md` item 4](../../../RELEASING.md#4-the-four-snapshots-and-the-verdict-file-adjudicated) | The rule that a model-facing tool name may not be renamed or removed in a release a live session can cross |

## What is here

| Path | What it holds |
|---|---|
| `rig/` | The dummy MCP server, the Anthropic and OpenAI API stubs, the app-server driver, the per-run `--mcp-config` files and the shell drivers |
| `prototypes/` | The relay and the handover, and the Windows stdio-inheritance experiments underneath them: `ho/`, `ho-helper/`, `ho-install/`, `inherit-test/` |
| `logs/` | One `LAUNCH` line per spawned server process (`*.launches.log`), the app-server JSON-RPC traffic (`*.driver.log`), the handover role lines, the run markers, and the request captures |
| `out/` | Per-run metadata, the client's own `--debug-file` output, Codex's tracing log, and the streamed model output |
| `config/` | The three scratch client configurations the runs were pointed at |

## What was cut, and what it was cut from

⚠️ **Every request capture is trimmed, and none is kept whole.** Each
`*.requests.jsonl` is the body of every API request the client sent, so it is
*exactly what the model saw* -- and 60 KB of each 63 KB request is three things
that are not evidence and one of which is not ours to publish: the session's own
system-reminder, which is this repository's `CLAUDE.md` verbatim; the tool schemas
of whatever else was registered; and **the client's own system prompt**, which
belongs to a third party. All **69** are kept as `*.requests.trimmed.jsonl` with
three cuts, applied wherever they occur:

- the client's own `system` prompt, replaced by `[cut: the client's own system prompt]`;
- any text block containing `<system-reminder>`, replaced by `[cut: the session's system-reminder, which is this repository's own CLAUDE.md]`;
- every tool definition reduced to `{"name": ..., "cut": "description and schema"}`.

**Everything else is byte-for-byte**, including the turn number, the timestamp,
the URL, the message order, every `tool_result` and every tool NAME -- which is
the half the findings turn on, because what is being established is which tools a
re-launched server's session still believed in.
[`logs/requests-originals.sha256`](logs/requests-originals.sha256) carries the
SHA-256 and the byte count of all **69** originals, so a trimmed file can be
checked against the thing it was cut from if one is ever taken again.

⚠️ **The client-side logs are kept for one round per scenario.** Each scenario
was run three times and every round's `launches.log` and request capture is
here; the 29 KB `--debug-file` log and the Codex tracing log are kept for the
**first** round only. The repeats establish *3/3*, and they establish it through
the launch lines, which are the thing being counted.

⚠️ **Four things a reader might look for are deliberately absent.**

1. **The two `openai/codex` source clones.** 234 MB of checkout, used to read the
   reconnect surface, and neither is a measurement. They were
   `openai/codex` at tag `rust-v0.155.0-alpha.9.2`, commit
   `4607249e430dac1c961df4dc615beae88e33cec8`, and `main` at
   `5f371ba30ae4a4bf4527eb0729ab6b36e3a0c123` read 2026-09-23T21:35Z. The finding
   they produced -- `reusable_client` rejects a client whose transport is closed
   on any refresh, and no production caller fires one on the failure path -- names
   `connection_manager.rs:93-107`, and is identical in both.
2. **A 1 MB slice of `claude.exe`** at offset 230,400,000, which is the client's
   MCP module including `ensureConnectedClient` and the `onclose` branch. It is a
   slice of a third-party binary, so it is described in the kb entry and not
   copied here.
3. **`code.claude.com/docs/en/mcp` and `anthropics/claude-code`'s `CHANGELOG.md`**,
   fetched 2026-09-23 and cited by URL.
4. **Empty files.** 42 `*.stderr.txt` were zero bytes, which is itself a finding
   -- the server's stderr reaches the client's debug file and nothing else -- and
   is recorded in the kb entry instead of as 42 empty files.

⚠️ **Codex colours its tracing output, and the escape sequences are stripped.**
Three `CXAR*.driver.log` captures and one read dump carried ANSI SGR sequences;
what is stored is what a terminal would have shown, with the escapes removed and
nothing else touched. The tree refuses a C0 control byte in text, and a captured
terminal transcript is the one place they arrive honestly.

**Line endings are this repository's**, per
[the directory's own note](../README.md): captures written with CRLF are stored
with LF.

**The maintainer's decision Q295 a rewrote the history on 2026-09-24 to correct
the author and committer address on 22 commits, so the `latest_git_commit_hash`
values these captures keep byte for byte name commits that now carry new
hashes:** `042e2fee5e6d49c68505d4d8c781b1622ba88e24` is now
`92733e9b0e622ee952c74ce706e75910accedcc0`,
`08e8d6163c767a4df09a5f24a90aa76bfd3c193c` is now
`ca2b28cd5ddd281d6369f11a6a8f9164076623b2`, and
`660af6d3302182eba6e7b4a28ea5fbffc56b5d37` is now
`3e4998db40f5e63f280e6c5158bb250fd8c7d823`.
