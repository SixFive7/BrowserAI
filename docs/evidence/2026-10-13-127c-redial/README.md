<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-13 - a real Claude Code starting BrowserAI's relay again, with the record of its tool list the same and different

**What this is.** The maintainer's 127 c, measured once, end to end, as his 151 b of
2026-10-13 asks, in his words verbatim *"151 b"*: with a scratch `CLAUDE_CONFIG_DIR` and
`CODEX_HOME`, under the suite lock, and never against the installed BrowserAI. Taken
2026-10-13 between 10:34:12Z and 10:34:36Z with
[the rig](../../probes/2026-10-13-127c-redial/README.md) as committed at `2fb8932d`, against
the relay published from this branch at `58dc4c0b`, version **2.0.1-alpha.0.12**, whose
product code `2fb8932d` leaves unchanged. Clients: **Claude Code 2.1.296**, the installed
CLI under `claude -p`, and the binary the VS Code extension 2.1.296 bundles, through the
stream-json transport the extension drives, each answering `--version` with
`2.1.296 (Claude Code)` under a scratch configuration. Windows 11 Pro 10.0.26300.

**How each run went.** The client started the published relay itself, from a registration
naming it with a pipe and a data root of the run's own, and listed its tools. A stand-in
background on that pipe answered the relay's greeting, the replayed `initialize` and the
calls, and ended the relay with `browserai/end` right after the first call, the way an
update ends a relay. The model stub scripted four calls of `browserai_list`. The client
started a second relay for the second call, and that relay was asked for no tool list. In
the `differ` mode the stand-in first wrote another digest into the record the first relay
had written.

**What it found** (`summary.txt`, `runs.tsv`):

- **`same`, 6 of 6** (3 under `claude -p`, 3 through the stream-json transport): two relays
  greeted the stand-in, both naming the client the driver started as their parent and one
  creation time for it; the first answered call 1, the second answered calls 2, 3 and 4;
  the model was handed four answers and **no stale-list refusal**; and the second relay
  logged `BrowserAI.Relay[27]`, *the client was answered this same tool list by build
  2.0.1-alpha.0.12*. One record was under the data root, named for that client process.
- **`differ`, 6 of 6**: the same two relays and the same parent; the model was handed the
  first call's answer, then **Q261 b's refusal once**, in its Claude Code words, then the
  answers to calls 3 and 4 from the second relay, which logged `BrowserAI.Relay[6]` with
  the reason *the one its client was last answered differs from this relay's*, and no
  `Relay[27]`.
- **No run met a closed pipe**: the client started the second relay for the call after the
  first relay ended, in every run.

**The first attempt, at 10:29Z, is kept under `attempt-1/`.** Its `print-same-r1` and
`print-differ-r1` held as the second attempt's did. Its `stream-same-r1` set
`CLAUDE_CODE_ENTRYPOINT=claude-vscode`, as the extension sets it, and the extension's
binary answered *"Not logged in · Please run /login"* (`client-result.txt`) and never
asked the model stub, which ended the attempt there. The rig sets no entrypoint since
`2fb8932d`, which leaves the transport as it was.

**What it does not establish.** No real background was in these runs, and no update: the
stand-in is the background, and `browserai/end` is the end an update sends. The two
records' lists were this build's and a digest written by hand, not two builds' lists.
Hosts not run: the terminal UI, which never starts a dead server again by itself and lists
on Reconnect ([kb](../../../kb/mcp/protocol.md)), Codex, which lists on every launch, the
Agent SDK, the Claude desktop app and JetBrains.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: protocol](../../../kb/mcp/protocol.md) | The paragraph of 2026-10-13 on a relay Claude Code starts again, under its re-launch section |
| [kb: re-verification](../../../kb/re-verification.md) | Row 149's fact cell, by addition |
| [`DECISIONS.md`](../../../DECISIONS.md) | The 127 c row's built note |

## What is here

| Path | What it holds |
|---|---|
| `summary.txt`, `runs.tsv` | The measurement's counts and one row per run, as the rig wrote them |
| `runs/<run>/summary.json` | What `summarize.ps1` read out of the run |
| `runs/<run>/tool-results.txt` | Every tool result the model was handed, in order, each cut to its first line, read out of the model stub's request log by `cut.ps1.txt` |
| `runs/<run>/standin.trimmed.jsonl` | The stand-in's log: each relay's greeting with its pid, its client's pid and creation time, and every frame |
| `runs/<run>/relay-browserai-*.trimmed.log` | The relays' own log, out of their data root |
| `runs/<run>/record-<client>-<created>-<command line digest>.json` | The record the first relay wrote, and in `differ` the stand-in rewrote |
| `runs/<run>/run.trimmed.json`, `driver.log`, `model.events.log` | What started and when |
| `attempt-1/` | The first attempt's three runs, cut the same way, and the stream-json run's `client-result.txt` |
| `cut.ps1.txt` | The script that cut this batch, stored with `.txt` appended |
| `originals.sha256`, `left-out.sha256` | The SHA-256 of every file kept or left out, each as it was read |

## What was cut, and what was left out

- **The user profile's path is replaced by `%USERPROFILE%`** in every kept file that carried
  it, under the `.trimmed.` name, with the original's digest in `originals.sha256`: the
  relays' data root and the CLI's path are under the profile.
- **Left out one file at a time, with digests in `left-out.sha256`**: each run's model stub
  request log, about 900 KB a run, whose tool results `tool-results.txt` carries; the
  client's output, its debug log, the registration and the three processes' console
  output; and the client pid, which `summary.json` checks.
- **Left out by directory, with file counts and bytes in `left-out.sha256`**: each run's
  scratch client configuration, Codex home and project folder.

## Re-establish it

Run [`measure.ps1`](../../probes/2026-10-13-127c-redial/README.md) from a worktree whose slice
is published, under the suite lock, into an empty folder, and compare its `summary.txt` and
`runs.tsv` with these.

## Privacy

Nothing kept here names the user, the user profile or the machine. Every model request
went to a stub on `127.0.0.1`, and the API key the clients were given is the literal
`stub-key-not-real`.
