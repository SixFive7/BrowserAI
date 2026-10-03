<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- what a client does to a stdio MCP server when the session ends (Q356 a)

**What this is.** 491 measured runs, taken 2026-10-03 between about 01:00Z and
04:20Z, of what Claude Code and Codex do to a stdio MCP server when a session
ends: whether its stdin reaches end of file, whether and when it is killed, and
how long it has in between. Taken at **Claude Code 2.1.288** (the CLI) and
**2.1.287** (the binary the VS Code extension ships), **codex-cli
0.155.0-alpha.9.2** and **0.160.0**, on Windows 11. Every client ran under a
scratch configuration against a local API stub. **278 files beside this
README, 5,907,703 bytes**, of which `runs.zip` is 3,430,014. The rig is a probe
record at
[`docs/probes/2026-10-03-client-exit`](../../probes/2026-10-03-client-exit/README.md).

[`INDEX.txt`](INDEX.txt) is the researcher's own map of the rig, the scenarios
and the batches, and is the file to read first.
[`table-by-scenario.tsv`](table-by-scenario.tsv) is the result in eighteen lines.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md#what-each-client-does-to-a-stdio-server-when-the-session-ends----measured-2026-10-03) | Every number in the section, and the correction of the earlier sentence that the client's tree kill leaves nothing of the server running |
| [kb: re-verification](../../../kb/re-verification.md) | Row 169 |
| [kb: what is not established](../../../kb/not-established.md) | The rows on the VS Code extension and the Codex desktop app, which were not driven |

## What is here

| Path | What it is |
|---|---|
| `INDEX.txt` | The rig, the binaries, the scenario letters, the batches and the code read |
| `table-by-scenario.tsv`, `summary-all.tsv`, `ranges.txt`, `summary-*.tsv` | The results: per scenario and per delay, whether end of file reached the server, how it died, and the timing ranges |
| `analysis-<batch>.tsv` | One row per server instance per run, for every batch: what it received, its end of file, its outcome, its stand-in's, the client's exit, and every other process the harness saw die near it, with the killer's creation time against the end of file |
| `runs/<batch>/<run>/` | One whole run per scenario and batch, 29 of them, client logs included: `harness.log`, `server-<pid>.log`, `standin-<pid>.log`, `result.json`, the client's own stdout, stderr and debug log, and the configuration the client was handed |
| `runs.zip` | **Every run, 2,218 files**, without the three client-side logs, byte for byte; SHA-256 `00214357f652491fbb3731c59ac29f807fdb18d795725090876253b277d9cb58` |
| `stub/*.log` | What each local API stub was asked, per batch |
| `code/INDEX.txt`, `code/codex-*-lines.txt` | Where the client code was read: byte offsets into the two Claude Code bundles and `extension.js`, and `file:line` into `openai/codex` at both tags |
| `binaries.trimmed.tsv`, `real-config-hashes.trimmed.tsv` | The SHA-256 of each client binary, and of the real Claude Code and Codex configuration before and after, with the profile path cut |
| `originals.sha256`, `left-out.sha256` | What was changed and what was left out whole, each with its SHA-256 |

**The scenario letters.** `a` is `claude -p` finishing; `b1` and `b2` drive the
CLI in stream-json mode and end by closing its stdin or by terminating it; `c1`
and `c2` do the same with the VS Code binary; `d1` is `codex exec`, `d2` the
app-server with its stdin closed, `d3` the app-server terminated; `e1` to `e3`
are the interactive TUI ended by `/exit`, Ctrl+C twice and the terminal closed;
`e4` interrupts an in-flight call and then closes stdin; `e5` and `e6` are the
VS Code extension host exiting and the SDK's `close()`, through a stand-in. A
run's `-d<ms>` is how long the server waited after its end of file before it
exited by itself. `cc` ran with an API key the client had not approved, so the
turn failed and the server still connected; `cc2` repeats it with the key
approved. `controls` plants every signature the analysis reads, and `keeper`
probes a process started outside the server's job.

**The real configuration changed, and not through the rig.** The real
`~/.claude.json` hashes differently after the work than before. Every client the
rig started had a scratch configuration; the Claude Code sessions running on
the machine rewrite that file for their own bookkeeping, which is the likely
cause and was not checked. The real Codex `config.toml` is identical.

## What was cut, and what was left out

- **The profile path** in `binaries.tsv` and `real-config-hashes.tsv`, replaced
  by `%USERPROFILE%`. Both are stored under a `.trimmed.` name, and
  `originals.sha256` carries each original's digest.
- **The client-side logs of all but the 29 unpacked runs**: 1,030 files,
  120,002,574 bytes of `claude-debug.log`, `client-stdout.log` and
  `client-stderr.log`, most of it Codex trace output. Four of the unpacked runs'
  own Codex traces, over 400 KB each, are left out the same way; their digests
  are in `left-out.sha256`.
- **The excerpts of client code**: twenty slices of Claude Code's bundle, the
  VS Code binary's bundle and `extension.js`. They are a third party's code;
  `code/INDEX.txt` gives each offset and what was read there, and
  `left-out.sha256` the digest of each slice.
- **The client binaries** under `bin\`, 910 MB, whose digests are in
  `binaries.trimmed.tsv`; the npm install of codex-cli 0.160.0, the npm cache,
  the two `openai/codex` checkouts (`rust-v0.155.0-alpha.9.2`, commit
  `4607249e430dac1c961df4dc615beae88e33cec8`, and `rust-v0.160.0`), the rig's
  build output and the scratch client homes.
- **Two drafts of upstream reports**, which are for the maintainer and not a
  record, with the issue templates and searches behind them and the text scan
  that checked them.
- **Three intermediate analyses** that the final file of the same batch
  replaces, and the shake-down batches' client logs.

## Privacy

Nothing here names the user or the user profile. **The machine name appears in
Claude Code's own debug log**, 65 times across the unpacked runs, in a line
reading *"sweep permitted (domain win32:..."*; the tree already carries that
name, and it was left as written. The API key the stubs saw is the literal
`stub-key-not-real`. The privacy scan run before the commit found nothing else,
and its positive control found all nineteen planted needles.
