<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-11-18 -- RegisterAI ended while it starts a client leaves that client suspended

**What this is.** Two runs of a rig, taken 2026-11-18 at about 06:29Z and 06:30Z on Windows 11
Pro 10.0.26300, after the final gate on `e3cc3a4f` left a `codex.exe mcp add browserai -- ...`
behind in its PowerShell half: pid 97312, created 05:38:39Z, its parent gone, its one thread
suspended and no CPU time, ended by pid at about 06:20Z by whoever was running that gate. The rig
puts the payload's RegisterAI 0.3.0 (SHA-256
`4200388652296856edb94c81f25c92a4cf8502ac202803248b754bdb5400431d`) in a job object it created,
with a completion port, and ends RegisterAI by its own handle the moment the job reports the
client RegisterAI is starting: the forced order "RegisterAI is ended while it starts a client",
which is what BrowserAI's `RegisterAiTool` does to RegisterAI at its wait. The client is a copy
of `whoami.exe` named as Codex with RegisterAI's own `--client-exe`, so no client ran and no
configuration was read or written; `CODEX_HOME` and `CLAUDE_CONFIG_DIR` named scratch folders
all the same. Everything ran at below-normal priority. Beside the rig, the console log of each half of
that gate, which the hazard rows on its two reds of this kind quote. **11 files beside this README,
93,150 bytes as this repository stores them.**

| Trials | When RegisterAI was ended | The client, 3 s later |
|---|---|---|
| armed-01 to -10 | 0.24 to 1.36 ms after the job reported the client | **Still running, its one thread `Wait`/`Suspended`, 0 ms of CPU, 10 of 10**; it never made its console host |
| control-01, -02 | Not ended | Ran and exited: 46.9 ms of CPU each, its console host made 422 and 570 ms after it |
| late-01 to -05 | After a sleep of 100 ms from the job's report of the client: 101 to 116 ms after it | Ran and exited, 46.9 to 78.1 ms of CPU, 0 of 5 left |

RegisterAI starts a client with `Process.Start`, `ProcessRunner.RunAsync` in its
`src/RegisterAI/Runner.cs` at `v0.3.0`, which asks for no suspended start. A client held
suspended by the call itself, and resumed only when the call ends, is what the three rows
show: ended inside the call, RegisterAI leaves a client that never runs; ended once the call has
returned, the client runs.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: processes](../../../kb/windows/processes.md#a-caller-ended-inside-createprocessw-leaves-its-child-suspended-for-good----measured-2026-11-18) | The three kinds of trial and what each left |
| [re-verification row 231](../../../kb/re-verification.md) | The fact and how to re-check it |
| [`HAZARDS.md`](../../../HAZARDS.md#hazard-index) | The row on RegisterAI ended at BrowserAI's wait while it starts a client; the sighting added to the row on the real Codex arm's RegisterAI run; the row on the real Claude Code arm that dialled nothing |

## What is here

| Path | What it is |
|---|---|
| `KillAtChild.cs.txt` | The rig: the job, its completion port, the end of RegisterAI at the client's creation, and the read of every process of the job by the handle opened at its creation |
| `run-trials.ps1.txt` | The driver: RegisterAI's `status` for Codex with the stand-in as its client, the trials, and the end of what is left through the rig's own job |
| `trials.json` | The armed and control trials: each process of the job, its threads 3 s after, the job's messages, and RegisterAI's document where it wrote one |
| `trials-late.json` | The late trials, the same fields |
| `trials.out.txt`, `trials-late.out.txt` | What each run printed |
| `order.py.txt`, `order.out.txt` | The order of the job's messages per trial, read out of the two JSON files |
| `gate-e3cc3a4f-powershell.out.txt`, `gate-e3cc3a4f-gitbash.out.txt` | What each half of the final gate on `e3cc3a4f` printed: its failures, with the Codex arm's in both halves and the real Claude Code arm's in the PowerShell half, and its coverage block with the commit charge |
| `originals.sha256` | The SHA-256 of each file as taken, under its name as taken |

## What was changed, and what was left out

- **The rig, the driver and the reader are stored with `.txt` appended**, as a record and not
  as this repository's code; the outputs are renamed the same way, and the two gate logs are
  named for their half; `originals.sha256` gives each log's digest under the name it is kept by
  here. Line ends are LF, as the
  repository stores text.
- **The armed and control trials ran before `DelayMs` and `-Late` were added** to the rig and
  the driver for the late trials. With no delay the stored rig does what the first one did.
- **Left out**: the stand-in, a copy of `C:\Windows\System32\whoami.exe` named
  `codex-standin.exe`, and the empty scratch folders the driver made.
- **Nothing is left running**: every process a trial left was ended through the rig's job
  object, `TerminateJobObject`, and each run's last column counts zero still running after it.

## How to run it again

From PowerShell 7, at below-normal priority, with the payload of a build that carries RegisterAI
0.3.0: `pwsh -File run-trials.ps1 -Armed 10 -Controls 2`, then
`pwsh -File run-trials.ps1 -Armed 0 -Controls 0 -Late 5 -LateDelayMs 100 -Out <another folder>`,
with `KillAtChild.cs` beside the driver. Nothing shows a window, and nothing outside the rig's
job is acted on.
