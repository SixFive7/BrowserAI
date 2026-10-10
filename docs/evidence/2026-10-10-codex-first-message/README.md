<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-10 -- a Codex thread's first message, and what a read of a SQLite database leaves beside it

**What this is.** Two measurements, taken 2026-10-10 by lane ID on Windows 11 Pro
10.0.26300, for 1.4 a's middle step as the maintainer approved it: a Codex thread that
Codex's index does not name is called by its first message. `measure.py.txt` read the 18
rollouts in the six scratch Codex homes of
[the measurement of 2026-10-08](../2026-10-08-client-tabs/README.md), codex-cli 0.162.0,
against the `threads` table of a copy of each home's `state_5.sqlite` and its `-wal`. The
measurement's own files were never opened with SQLite, because an open writes to a
database's `-shm`. `sqlite-files.py.txt` built one database in write-ahead-log mode in an
empty scratch folder and listed the folder's files after each of four opens, with
Python's SQLite 3.50.4. **5 files beside this README, 13,167 bytes as this repository
stores them.**

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md) | "A Codex thread's first message" |
| [`DECISIONS.md`](../../../DECISIONS.md) | The row on 1.4 a's middle step, under telling the clients' conversations apart |

## What is here

| Path | What it is |
|---|---|
| `measure.py.txt`, `measure.out.txt` | One row per rollout: its thread, its home (1 to 3 are the homes under `h`, 4 to 6 those under `runs/cx`), Codex's version, originator and source, the rollout's length, where its first `UserMessage` record starts, whether that message equals the thread's `title`, whether the thread has a `name`, how many `UserMessage` records it holds, and how many `user`-role messages Codex wrote itself; then the totals |
| `sqlite-files.py.txt`, `sqlite-files.out.txt` | The four opens of one database and the folder's files after each |
| `originals.sha256` | What was changed, each with the SHA-256 of what it was |

## What was changed, and what was left out

- **Both scripts are stored with `.txt` appended**, as a record and not as this
  repository's code.
- **Both outputs have their line ends made LF**, as the repository stores text.
- **Left out**: the six homes and the copies of their databases the first script read,
  which hold the rig's own prompts and Codex's own instructions, as the batch of
  2026-10-08 leaves the homes out. They stay in `.work/client-tabs` on the machine that
  took them, which is not a record.

## Privacy

The scripts take their folders as arguments and print no path, and every thread and
message they read is the rig's own. The privacy scan lane ID ran over the batch of
2026-10-08, with its positive control, found nothing in the five files.
