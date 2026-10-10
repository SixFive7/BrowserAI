<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-08 -- which conversation of a client started a stdio MCP server

**What this is.** The measurement, taken 2026-10-08 between 22:30Z and 23:00Z on
Windows 11 Pro 10.0.26300 by an agent of the root session, of how a stdio server can
tell several conversations of one client apart, down to several Claude Code tabs in
one VS Code window, and name each the way the person sees it: 21 emulated VS Code tabs
in 2 windows over 3 repetitions, 18 runs of the terminal UI, 15 of `claude -p` and 18
of Codex, against Claude Code 2.1.295 and the extension's own 2.1.292 and codex-cli
0.162.0. The client-id stub, extended, recorded everything it was sent and, once a
second and at every call, what it could read of the client's own records; every client
ran under a scratch configuration against local model stubs, and no window was shown.
**38 files beside this README, 257,185 bytes as this repository stores them** (*corrected
2026-10-10, previously "256,875 bytes", by the cut of a session id below*). It is
what the maintainer's answer of 2026-10-10 was given on, and what lane ID built from.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md) | "Which conversation of a client started a server" |
| [kb: re-verification](../../../kb/re-verification.md) | The row that entry carries |
| [`DECISIONS.md`](../../../DECISIONS.md) | Telling the clients' conversations apart, decided 2026-10-10 |

## What is here

| Path | What it is |
|---|---|
| `FINDINGS.md` | The agent's report as it stands in the measurement's folder; its first line says why it is in this form |
| `runs/vs-tabs.tsv` | One row per emulated VS Code tab: its window, its arguments, what each source said at each call and at the end, and what the tab showed |
| `runs/terminal-runs.tsv`, `runs/print-runs.tsv`, `runs/codex-runs.tsv` | One row per run of the terminal UI, of `claude -p` and of Codex, the same way |
| `runs/score.txt` | Every way of naming a conversation scored against what the person saw, at each call and at the end |
| `rig/` | The stub and its view of the client's records (`TabRig/IdStub.cs.txt`, `TabRig/Views.cs.txt`), the harness and the pseudoconsole, the VS Code driver that stood in for an extension host (`vsdrive.js.txt`), the batch writers, the model stubs, the analyses, and the read-only live probes |
| `originals.sha256` | What was changed, each with the SHA-256 of what it was |

## What was changed, and what was left out

- **Every source file and script is stored with `.txt` appended**, as a record and not
  as this repository's code, the way the 2026-10-08 batches before it store theirs.
- **The profile path in one line of `rig/common.js`** is made `%USERPROFILE%`, kept as
  `rig/common.trimmed.js.txt`.
- **`FINDINGS.md` has the two SPDX header lines prepended.** Every change is in
  `originals.sha256` with the original's digest.
- ⚠️ *Added 2026-10-10 by addition.* **A session id is cut from `FINDINGS.md`**: line 171
  gave the first eight hex digits of the `--resume` argument of one of the maintainer's
  own VS Code tabs, and they are replaced by `<session id>`, at his answer of 2026-10-10,
  verbatim *"18 b"*. The file it was cut from is `FINDINGS.md` as this batch first stored
  it, SHA-256 `f36f1c6be4a64e38d716d1521a65de6eb244646a2343bb154e03445949c5c07d`, which
  `originals.sha256` records in a line of its own beside the original's digest. What the
  line supports, that the tab's own file and its `--resume` named one session, does not
  depend on which session it was. The first stored form stays in the history of commit
  `27e04356`, which a cut in the tree does not reach.
- **Left out by directory**, with no digest per file: the client binaries, 50 files and
  972,884,507 bytes; every run's captures and the clients' scratch configurations and
  homes, 2,819 files and 109,301,189 bytes, which hold the system prompts each client
  sent the local model stub and the scratch transcripts; the live readings of the
  maintainer's own sessions, 9 files and 43,509 bytes, which the agent wrote as
  structure only and which name his live processes; the scratch homes under `h`,
  11,751,721 bytes; and the scratch app data, the project folders and
  the temporary folder. They stay in `.work/client-tabs` on the machine that took them,
  which is not a record.

## Privacy

Nothing here names the user profile or the machine. A scan for the profile path, the
user name, the machine name, the maintainer's e-mail addresses and the names of his
other projects found nothing in the files beside this README after the one cut above,
and its positive control found every planted needle. The titles and prompts in the
tables are the rig's own, written for the measurement.

⚠️ *Added 2026-10-10 by addition.* **That scan had no needle for a session id**, which is
how the one cut on 2026-10-10 stood through it. The search that followed it read every
file of every batch under `docs/evidence`, 7,481 files, for the cut id's first eight and
last twelve hex digits, as ASCII and UTF-8, UTF-16 in both byte orders, either case and
inside zip archives at any depth; its positive control found all six planted forms and
nothing in a clean file. It found the one occurrence cut above and no other, and the 987
other files the repository holds carry neither part.
