<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-11-17 -- how long BrowserAI's registration with Codex takes (174 b)

**What this is.** 25 timed runs, taken 2026-11-17 between 15:04Z and 15:08Z, of the
command line `McpRegistrar` builds, run with the RegisterAI 0.3.0 that BrowserAI 2.0.0
installed (SHA-256 `4200388652296856edb94c81f25c92a4cf8502ac202803248b754bdb5400431d`,
the release's own) from its own folder, against the Codex it finds, codex-cli
0.166.0-alpha.6 at `~\.codex\plugins\.plugin-appserver\codex.exe`, and Claude Code 2.1.326
at `~\.local\bin\claude.exe`. Windows 11 Pro 10.0.26300, up since 2026-11-13T10:14:07Z.
Each run had a fresh scratch `CODEX_HOME` and `CLAUDE_CONFIG_DIR`, the session's own
`CLAUDE*`, `ANTHROPIC*`, `CODEX*`, `OPENAI*`, `MCP_*`, `ELECTRON_*` and `VSCODE_*`
variables removed, normal priority, and one run at a time. Nothing was written to the
real `~\.codex`, `~\.claude`, `~\.claude.json` or the install.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md#how-long-browserais-registration-with-codex-takes----measured-2026-11-17) | Every number in the section |
| [kb: numbers](../../../kb/numbers.md) | The evidence of `ProcessBounds.RegisterAiTimeout` |
| [kb: re-verification](../../../kb/re-verification.md) | Row 226 |
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The addition of 2026-11-17 to the row on the Codex arm's registration |

## The runs

| Folder under `runs/` | Condition |
|---|---|
| `run01-all-first-since-0645Z` | Both clients. The first start of that `codex.exe` since 06:45:10Z that day, read off its last-access time and the `tmp\arg0-windows` folder it left in the real Codex home; no process ran from it, read off every process's image path |
| `run02-all-warm-01` to `-10` | Both clients, warm |
| `run03-codex-warm-01` to `-10` | Codex alone, warm |
| `run04-codex-freshcopy-first` | Codex alone, named with `--client-exe` to a copy of the same file made at a new path about 3 s before: the first run of a file Windows had never run |
| `run05-codex-freshcopy-warm-01` to `-03` | The same copy, warm |

Each folder holds `summary.json`, the rig's record of the run: the command line, the
variables removed, the codex file's length and time, the processor queue length, the
commit charge and the uptime before and after, every process RegisterAI started with the
kernel's creation and exit times and its exit code, and the job's messages; and
`stdout.json`, RegisterAI's own document. Every run's stderr was empty.
[`table.txt`](table.txt) is one line per run, [`stats.txt`](stats.txt) the minimum,
median and maximum per group. `console/` holds what each run printed.
`rigcheck/` is the check of the rig before the first measured run: `whoami.exe` named as
Codex with `--client-exe`, so that no client ran and the job still reported RegisterAI's
children.

## What was read without running anything

The codex file is 371,731,248 bytes, written 2026-11-05T02:11:03Z, SHA-256
`925c7d0665ffa49b0dcf748e034a805a04da984164ac92301432073b47789b99`, with no version
resource. RegisterAI takes the first entry of
`%LOCALAPPDATA%\OpenAI\Codex\chrome-native-hosts-v2.json` whose `codexCliPath` exists;
that entry names `cliVersion` `26.1002.52244`. The file's own `--version`, asked under a
scratch home after the first run, answered `codex-cli 0.166.0-alpha.6`.

## The rig

`rig/` holds the scripts as they ran, under `.txt` names so that nothing in the suite
reads them as this repository's code: `JobWatch.cs` puts RegisterAI in a job object with a
completion port and opens a handle on each process as it is created, `run-registerai.ps1`
takes one run, `series.ps1` serial runs, `tabulate.ps1` and `stats.ps1` the tables, and
`cut.ps1` cut this batch. It selects no process by name; the one process listing it makes
compares image paths with the codex file's path.

## What was cut

The scratch homes of every run, a `config.toml` in each Codex home and a `.claude.json`
with its backup in each Claude Code configuration, and the empty stderr files, are left
out; [`left-out.sha256`](left-out.sha256) lists each with its SHA-256. The console
outputs and the rig are kept under `.txt` names; [`originals.sha256`](originals.sha256)
gives the digest of each as taken. Line endings are this repository's, LF.
