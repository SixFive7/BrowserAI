<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-11-17 -- what a client does to a stdio MCP server when the session ends, again (176 a)

**What this is.** The maintainer's 176 a: the measurement of
[`2026-10-03-client-exit`](../2026-10-03-client-exit/README.md) taken again with the same
rig, [`docs/probes/2026-10-03-client-exit`](../../probes/2026-10-03-client-exit/README.md),
built with SDK 11.0.100 for its own `net10.0` and run on runtime 10.0.15, on 2026-11-17
between 15:16Z and 15:25Z. **Claude Code 2.1.326**: the CLI at `~\.local\bin\claude.exe`
and the binary the VS Code extension 2.1.326 ships are the same file, SHA-256
`44e85ac6d3e36b924a0d631d3f69c13a6907491115d71e97c8eb6160c82b2aed`, so the stream-json
runs stand for both. **codex-cli 0.166.0-alpha.6**, SHA-256
`925c7d0665ffa49b0dcf748e034a805a04da984164ac92301432073b47789b99`. Both ran from copies
under the scratch root, with the batch's scratch `CLAUDE_CONFIG_DIR`, `CODEX_HOME`,
`APPDATA`, `LOCALAPPDATA`, `TEMP` and `TMP`, against the probe record's local API stubs,
so no model was called; the Claude Code configuration's `settings.json` refused this
repository's four project servers and every run passed `--strict-mcp-config`. The rig and
the clients ran at below-normal priority, which the clients inherit.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md#what-each-client-does-to-a-stdio-server-when-the-session-ends----measured-2026-10-03) | The correction of 2026-11-17 for Claude Code and the verification for Codex |
| [kb: re-verification](../../../kb/re-verification.md) | Row 169 |

## The batches

| Batch | Runs | What |
|---|--:|---|
| `controls` | 15 | The probe record's controls, its batch file with the scratch root moved: a clean close, a self-exit, `taskkill`, `TerminateProcess`, and `taskkill` then the close, three each. Every signature came out as in [the recorded analysis](../2026-10-03-client-exit/analysis-controls.tsv) |
| `cc3` | 42 | Claude Code: `a`, `claude -p` finishing, and `b1`, the stream-json input closed, at delays of 0, 100, 200, 500, 1,000, 3,000 and 15,000 ms, three each |
| `cx3` | 12 | Codex: `d1`, `codex exec` finishing, and `d2`, the app-server's stdin closed, at 100 and 15,000 ms, three each |

`c1`, the stream-json runs through the VS Code binary, was not run: at 2.1.326 it is the
same file as the CLI's, with the same arguments and variables. The scenarios where the
client itself is killed were not run either.

## What is here

| Path | What it is |
|---|---|
| `analysis-controls.tsv`, `analysis-cc3.tsv`, `analysis-cx3.tsv` | The probe record's `tools/analyze.py` over each batch: one row per server instance per run |
| `cc3-taskkill.txt` | Each `cc3` run's outcome and the `taskkill` its client started, created and exited against the server's end of file, read out of the analysis's `late_children` column |
| `cx3-appserver-close.txt` | The probe record's `tools/appserver_close.py`, pointed at `cx3` |
| `batches/` | The three batch files the harness ran and their progress logs |
| `runs/` | Eight whole runs: a control, five Claude Code runs, among them `b1-d200-r3`, the one whose `taskkill` came at 2,103 ms, and one run of each Codex scenario without its trace log |
| `stub/` | What each local API stub was asked, per batch |
| `tools/` | The generator for `cc3` and `cx3`, which is the probe record's `gen2.ps1` with the scratch root moved, scenario `a` and the Codex scenarios taken over from `gen.ps1`, and the refusal of the project servers added; and `appserver_close.py` pointed at `cx3`. Both under `.txt` names |

## What was cut

Every other run of the three batches is left out, and so is the Codex runs' trace log,
`client-stderr.log`, 0.7 MiB a run; [`left-out.sha256`](left-out.sha256) lists each file
with its SHA-256. The two tools are kept under `.txt` names and
[`originals.sha256`](originals.sha256) gives each one's digest as it ran. Line endings are
this repository's, LF.
