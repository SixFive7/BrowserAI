<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- a headless Firefox launch, hundreds of times, in a job of its own

Establishes
[A headless Firefox that starts while Shift is held never finishes launching](../../../kb/playwright/provisioning-and-timings.md#a-headless-firefox-that-starts-while-shift-is-held-never-finishes-launching----measured-2026-09-25)
and re-verification row 168. Evidence:
[`docs/evidence/2026-09-25-firefox-safe-mode/`](../../evidence/2026-09-25-firefox-safe-mode/README.md).

**Why it exists.** Q302: a Firefox session sometimes stopped for three minutes
and then failed with *"Timeout 180000ms exceeded"*, in the suite and in the
cost-ratio rig, and nothing said why. The rig launches Firefox the way the
product's child does, many times, and records enough of each launch to tell a
stall from a slow start and to read what a stalled browser is doing.

## Eight files

| File | What it does |
|---|---|
| `run-batch.ps1` | Launches `-Count` headless Firefox sessions through the payload's `node.exe` and `playwright-core`, `-Batch` at a time, each node and Firefox tree in a `KILL_ON_JOB_CLOSE` job it creates, optionally with a commit limit (`-JobMemoryMB`) or a CPU cap (`-CpuRate`) on that job. Writes `results.jsonl`, `events.log` and `jobs.log` under `runs\<RunName>` |
| `run-ab.ps1` | The same, plus `-AbEnvName`: even-numbered launches get that variable and odd ones do not, interleaved launch by launch. And `-ForceEnv NAME=VALUE`, set on every launch, which is how the forced arms set `MOZ_SAFE_MODE_RESTART=1` |
| `Rig.cs` | The job objects, suspended launches and the look at a stuck browser's windows and threads, compiled by the two scripts with `Add-Type` |
| `ffprobe2.js` | One launch: `launchPersistentContext` with the product's launch options and two `firefoxUserPrefs`, a navigation to a local page, a close, and the process tree read for `-safeMode`. `ffprobe.js` is the first version, used by the `smoke` and `serial1` arms |
| `summary.py` | The table in the kb entry, from `results.jsonl` alone |
| `analyze.py`, `calllog.py` | Per-run markers out of `probe.log` (whether `Browser.enable` was answered), and the Firefox call log out of a suite log's failure text |

## What keeps it off the rest of the machine

- Every process it starts is in a job it created, with `KILL_ON_JOB_CLOSE`, and
  what it reads about a stuck browser it reads by the pid its own launch
  returned. **It selects no process by image name.** A search of the eight files
  for the eight spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on finds `Win32_Process` filtered on `ParentProcessId` and
  `Diagnostics.Process` reached through `GetProcessById`, both keyed on a pid
  the rig holds, and nothing that matches a name.
- The child's environment is built from an allowlist like the product's, so a
  variable in the person's own environment does not reach the browser unless an
  arm sets it.
- `PLAYWRIGHT_BROWSERS_PATH` points at a scratch folder holding a COPY of the
  provisioned Firefox. The real browsers root is only read.
- `-WaitForQuiet` holds each batch while `.work\installer.lock` exists or a log
  under `.work\suite` grew in the last 20 seconds; the parallel arms ran with it.
- Every launch is headless. No window was shown.

⚠️ **Firefox records every executable path it runs from** under
`HKCU\Software\Mozilla\Firefox\Launcher`, five values per path, and nothing
removes them. A run of this rig adds five for the scratch copy. Snapshot the key
before a run and remove only what the run added.

## Running it

Nobody ran it when this record was written; what follows is read from the
scripts.

```
pwsh -NoProfile -File docs\probes\2026-09-25-firefox-safe-mode\run-ab.ps1 -RunName forced -Count 3 -TimeoutMs 30000 -ForceEnv MOZ_SAFE_MODE_RESTART=1
pwsh -NoProfile -File docs\probes\2026-09-25-firefox-safe-mode\run-ab.ps1 -RunName ab -Count 200 -TimeoutMs 30000 -AbEnvName MOZ_DISABLE_SAFE_MODE_KEY
python docs\probes\2026-09-25-firefox-safe-mode\summary.py
```

**The forced arm is the one to run first.** It reproduces the stall three times
in three with no keyboard involved, so it says in two minutes whether the
behaviour still exists. An organic stall needs Shift to be down at a launch
instant, which is why a run of hundreds of launches can come back clean.

**The paths are compiled in.** The scripts name the repository as
`C:\Source\SixFive7\BrowserAI`, the Firefox copy as
`.work\firefox-launch\firefox-1549`, and their output as
`.work\firefox-launch\runs`, which is where the research ran. `summary.py` and
`analyze.py` read `..\runs` beside themselves. A run from another checkout or
against another Firefox needs those changed, and is a new measurement with a
date of its own.

## How the stored copies differ from the ones that ran

The two SPDX lines and the blank line under them were added to every file when
it was stored. Nothing else moved.
