<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- a coordinator the Task Scheduler starts, a host in its job, and every client's exit

Establishes
[A process the Task Scheduler starts keeps its job's processes through every client's exit](../../../kb/windows/processes.md#a-process-the-task-scheduler-starts-keeps-its-jobs-processes-through-every-clients-exit----measured-2026-10-03)
and its re-verification row. Evidence:
[`docs/evidence/2026-10-03-coordinator-survival/`](../../evidence/2026-10-03-coordinator-survival/README.md).

**Why it exists.** Q366 b: before building option c, measure whether a process the
coordinator starts is out of reach of every way a client ends its server. The
coordinator is started by the Task Scheduler and so is outside every client's tree
and job; what was not known was whether that holds for what it starts, and whether
Codex's job, which allows no breakaway, reaches it.

## What is here

**This is lane c's copy of [the client-exit rig](../2026-10-03-client-exit/README.md)**,
and only what was added or changed is kept here. The rest of that rig, its stubs,
`vscodehost.js` and its `Native.cs`, were used as they are recorded there.

| Path | What it is |
|---|---|
| `ExitRig/Coord.cs` | New. Five modes: `coord`, the coordinator stand-in, which creates a kill-on-close job, starts the host in it and serves a control pipe; `host`, which serves its pipe and starts a browser stand-in per request in a kill-on-close job of its own, nested in the coordinator's, and answers `status`, `release` and `list`; `ctl`, a pipe client; `watch`, which opens a handle on each pid it is given, checks its creation time and waits for its exit; and `kill`, which terminates one process by its pid and creation time |
| `ExitRig/Server.cs` | Changed: given `--host-pipe` and `--host-id`, the dummy server also asks the host for a browser named after its run, beside the stand-in it always starts in a job of its own |
| `ExitRig/Harness.cs` | Changed: after the settle it asks the host for that run's browser, again 3 s later, then releases it, and records the three answers in `result.json` |
| `ExitRig/Program.cs` | Changed: the five new modes |
| `ExitRigW/ExitRigW.csproj` | The same sources built for the Windows subsystem, as `BrowserAI.exe` is, which is what the task starts |
| `tools/task.ps1` | Registers, runs and removes the scratch per-user task: the sign-in task's own settings, the current user's SID as its principal, an interactive token, no trigger, run on demand |
| `tools/genc.ps1` | Writes a batch: the seven exits, three runs each, every run's server pointed at the host |
| `tools/drive.ps1` | The whole measurement: registers the task, starts the coordinator through it, runs the batch, starts two more browsers, kills the coordinator by its recorded pid and creation time while a watcher holds handles to them, and removes the task whatever happened |
| `tools/analyzec.py` | One row per run and the per-scenario summary |
| `originals.sha256` | The SHA-256 of each file as it ran, before the two-line header was added |

## What keeps it off the rest of the machine

- Every client ran with `CLAUDE_CONFIG_DIR`, `CODEX_HOME`, `APPDATA`,
  `LOCALAPPDATA`, `TEMP` and `TMP` in scratch, the base URL on a local stub and a
  key that is not a key, from the client-exit rig's copies of their binaries. No
  model was called and no real client configuration was touched.
- **The task is the one thing outside scratch.** It is registered for the current
  user alone, named `BrowserAI.c-probe coordinator <stamp>`, and removed in the
  driver's `finally`; every registration and removal is in the evidence's
  `task-actions.log`.
- **It selects no process by image name.** Every process it waits on or ends is
  named by a pid and a creation time it recorded; `kill` checks the creation time
  before it terminates. The harness's walk is the client-exit rig's, by parent pid.
  `Harness.cs` spells `taskkill` where the client-exit rig's does, which the scan
  flags there as a false positive; the other files carry none of the spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on.
- Nothing shows a window: the coordinator is a Windows-subsystem binary with none,
  and every console child is started with `CREATE_NO_WINDOW`.

## Running it

From a build of the client-exit rig with these files over it, both builds, the
console one to `rig\bin` and the Windows-subsystem one to `rig\binw`, and the two
stubs listening:

```
pwsh -NoProfile -File tools\drive.ps1
python tools\analyzec.py <the run directory drive.ps1 wrote>
```

The scripts carry the scratch root they ran from, `.work\c-scratch\m1`, and the
client-exit rig's binaries under `.work\client-exit`; a run anywhere else changes
those two lines.
