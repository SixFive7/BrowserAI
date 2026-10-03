<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- what each client does with a failed `tools/list`, and a Codex started before the install (Q296, Q304)

**What this is.** 51 client runs taken between about 22:45Z on 2026-09-24 and
01:58Z on 2026-09-25: what Claude Code and Codex do when a server answers
`tools/list` with an error at their first connection, as an updating BrowserAI
did, and the five shapes an updating server could take instead (Q296); and
whether a Codex started before or after the install finds a project entry
written as the bare `BrowserAI.Server.exe` (Q304). Taken at **Claude Code
2.1.282**, **codex-cli 0.155.0-alpha.9.2**, node v26.7.0 and BrowserAI
1.1.1-alpha.0.130 published from the tree, every client under a scratch
configuration against a local API stub, on Windows 11. **1,065 files beside
this README, 5,510,164 bytes as cut.** The rig is a probe record at
[`docs/probes/2026-09-25-client-behaviour`](../../probes/2026-09-25-client-behaviour/README.md).

`REPORT.trimmed.txt` is the researcher's report and the file to read first;
`runs-index.json` maps every run to its arm.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md#what-each-client-does-when-toolslist-fails-at-the-first-connection----measured-2026-09-25) | Every number in the section, and the addition that measures the Codex bare-name route |
| [kb: re-verification](../../../kb/re-verification.md) | Row 173 |

## What is here

| Path | What it is |
|---|---|
| `REPORT.trimmed.txt` | The report: every arm, its result 3 of 3, the byte-exact texts, the directions, the cleanup and the limits |
| `runs-index.json`, `texts.json` | All 51 runs with their arm and client; and every sentence the server sent and the model saw, byte for byte |
| `runs-out/` | What each run script printed |
| `runs/<run>/` | Per run: `summary.json`, the scripts and config it ran, the launch lines (one per server process), the wire and event logs, the app-server driver log, and every API request the client sent, trimmed as below. The client's own debug log and stream output are kept for the first round of each arm |
| `q304/` | The Q304 install and runs: each app-server's driver log, the install, lock and uninstall logs, and the clearance readings before, during and after |
| `originals.sha256`, `left-out.sha256` | What was changed and what was left out whole, with digests |

## What was cut, and what was left out

- **Every request capture is trimmed**, as in
  [`2026-09-23-client-reconnect`](../2026-09-23-client-reconnect/README.md): the
  client's own system prompt or instructions, every system-reminder block and
  every tool's description and schema are cut, and the rest is byte for byte,
  including every tool name and every `tool_result`. Under a `.trimmed.` name.
- **The profile path** is replaced by `%USERPROFILE%` wherever it occurred, in
  the report, the driver logs and the install logs, under `.trimmed.` names.
  `originals.sha256` carries the digest of each of the 182 originals.
- **The Q304 environment blocks and PATH readings**: 26 files listing this
  machine's environment and the user PATH, which name the software installed
  on it. The report quotes their digests, which is what it compares: the user
  PATH byte-identical before and after.
- **The app-server's stderr in 14 runs**, about 11.5 MB each of Codex trace
  output; their digests are in `left-out.sha256`.
- **The client logs of repeat rounds**: 80 files, kept for round one.
- **The scratch homes** of every run, each Codex home holding a clone of the
  plugin catalogue, the 127 MB copy of the server and the 60 MB test installer.

## Privacy

No file here names the user or the user profile. **The machine name appears**
in the Claude Code debug logs and the Codex driver logs, as their own server
name; the tree already carries it. The privacy scan, with a positive control,
found nothing else.
