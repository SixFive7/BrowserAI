<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-13 - which process starts a server Claude Code launches again, read from the Q369 batch

**What this is.** A reading, taken on 2026-10-13, of a measurement taken on 2026-10-03:
the Q369 batch, whose stand-in MCP server wrote one `LAUNCH` line per process it ran,
with that process's parent pid, and whose harness wrote the pid, the creation time and
the exit time of the Claude Code it started. The batch was kept whole in the main
checkout's scratch folder, `.work/q369`, and
[`2026-10-03-q369-tool-list-refresh`](../2026-10-03-q369-tool-list-refresh/README.md)
is its cut for the records of that day, which keeps nineteen runs and none of the ones
read here. The clients were **Claude Code 2.1.288**, the CLI and byte for byte the
binary the VS Code extension updated itself to that day, and **2.1.287**, the
extension's binary of the morning, under `claude -p` (`P` arms), the stream-json
transport the extension drives (`S` arms, `cli` and `vsc287`) and the terminal UI
(`T` arms); Windows 11.

**It answers one question for the maintainer's 127 c of 2026-10-12**: when Claude Code
starts a server again, is the new server the child of the same client process as the
one before it? A relay that records, for its client process, the tool list it answered
depends on it ([kb](../../../kb/mcp/protocol.md)).

**What it found** (`summary.txt`, written by `rig/redial-parent.ps1.txt` over the
batch):

- **27 runs launched the server again and sent the second launch no `tools/list`**:
  `PB`, `PBd` and `PBr` under `claude -p`, 9, and `SB`, `SBr` and `SBts` through the
  stream-json transport, 18. **In 27 of 27, both launches' parent was the Claude Code
  process the harness started**, created before the first launch and still running when
  the second started, 1,787 to 10,984 ms later.
- **The same held in 69 of 69 runs with two launches**, those 27 with the reconnects,
  `mcp_reconnect` in the `S` arms and the terminal UI's Reconnect in the `T` arms, and
  the `SK` and `TK` arms, whose first server was killed before its handshake finished.
- **Two positive controls**: the same rows compared with a client pid one higher hold in
  0 of 69, and of the 54 Codex runs with two launches or more, whose harness kept no
  client pid, 18 had launches with more than one parent, every one of them `codex exec`,
  which is started once per turn (`control-codex.tsv`).

**What it does not establish.** The parent's creation time was not written per launch;
the pid is the same process because that process ran from before the first launch until
after the second, and a running process's pid is not handed to another. Nothing here was
a BrowserAI relay: the stand-in was started by the client directly, as a registration
of BrowserAI is. The relay's own command line across the two launches was not written.
Hosts not run: the Agent SDK, the Claude desktop app and JetBrains.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md) | The paragraph on the parent of a server Claude Code launches again, under its re-launch section |
| [kb: re-verification](../../../kb/re-verification.md) | Row 149's addition of 2026-10-13 |
| `ToolListRecordFile`, `IToolListRecord` | Why a relay's record is keyed by its client process |

## What is here

| Path | What it holds |
|---|---|
| `summary.txt` | The counts above, as the rig printed them |
| `parents.tsv` | One row per Claude Code run with two launches: the client's pid, creation and exit, every launch's parent, the `tools/list` each launch answered, the gap between the launches, and both predicates |
| `control-codex.tsv` | One row per Codex run with two launches or more: every launch's parent and whether there was one |
| `runs/<batch>/<run>/ph.launches.log` | The stand-in's launch log of each of the 69 Claude Code runs, as it was written |
| `rig/redial-parent.ps1.txt` | The reading, a PowerShell script, stored with `.txt` appended so that nothing here is run or held to the rules for source files |
| `originals.sha256` | The SHA-256 of each launch log and of the script as they were read and run |
| `left-out.sha256` | The SHA-256 and the size of each run's `result.json`, read and left out |

## What was cut, and what was left out

- **Each run's `result.json` is left out whole**, with its digest and size in
  `left-out.sha256`. It lists every process the harness's observer saw while the run
  lasted, with its command line; what this reading took from it is the client's pid,
  creation time and exit time, which are in `parents.tsv`.
- **Everything else of the batch is left out by directory**: every other file of these
  69 runs, every run with one launch, and the Codex runs but for what `control-codex.tsv`
  carries. Its own cut, [`2026-10-03-q369-tool-list-refresh`](../2026-10-03-q369-tool-list-refresh/README.md),
  says what the batch holds.
- **The launch logs are the bytes as written**, but for the line endings this repository
  stores ([`docs/evidence`](../README.md)).

## Re-establish it

Run `rig/redial-parent.ps1.txt`, saved under a `.ps1` name, with `pwsh -NoProfile -File`
over a Q369 runs folder and an empty output folder, and compare its `summary.txt` with
this one. A new measurement is the batch's own rig, `rig/` in
[the Q369 cut](../2026-10-03-q369-tool-list-refresh/README.md), with the stand-in set to
end after its first call, every client under a scratch configuration and against the
local model stub, and the stand-in's `LAUNCH` line read for its `ppid`.

## Privacy

Nothing here names the user, the user profile or the machine. The launch logs carry the
batch's own paths under this repository's scratch folder, and the clients' `clientInfo`.
