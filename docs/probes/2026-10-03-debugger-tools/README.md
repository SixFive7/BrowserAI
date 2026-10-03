<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- arm a pause in a session, then try every way out

Establishes
[A pause armed from inside a session, and every way out](../../../kb/playwright/tools-and-artifacts.md#a-pause-armed-from-inside-a-session-and-every-way-out----measured-2026-10-03)
and re-verification row 172. Evidence:
[`docs/evidence/2026-10-03-debugger-tools/`](../../evidence/2026-10-03-debugger-tools/README.md).

**Why it exists.** Q321: `browser_resume` is in the surface BrowserAI forwards,
and before deciding what to do with it and its neighbours, measure how a pause
gets armed inside a session and what each tool does to the call it parks.

## What is here

| File | What it does |
|---|---|
| `rig.cjs` | Drives one `@playwright/mcp` child over stdio the way BrowserAI does, with a generated `--config`, `--sandbox` and an allowlisted environment, and runs one scenario: arm, park, try a way out, read the result, write `result.json` |
| `supervise.ps1` | Starts one rig process inside a `KILL_ON_JOB_CLOSE` job it creates, so everything the rig starts goes with it |
| `batch.ps1`, `waitrun.ps1` | Run scenario by family by run, one browser at a time, and wait on the newest run by the pid its supervisor recorded |
| `summarize.cjs`, `brief.cjs` | Flatten every `result.json` into one row per step and aggregate per scenario, family and step; print it compactly |
| `toolslist.cjs`, `difftools.cjs`, `audit-table.cjs` | Ask a child for `tools/list` with BrowserAI's capability grant, diff two lists, and write the 72-tool audit |
| `after.ps1`, `cleanup-launcher.ps1` | A read-only snapshot after the runs, diffed against the one before; and the removal of only the Firefox launcher values this rig's runs added |

## What keeps it off the rest of the machine

- Every browser is headless and runs from the shared scratch browser cache, with
  the registry, the temp folders and the app data in scratch.
- Every process of a run lives in a job its supervisor created. **It selects no
  process by image name**: of the eight spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on, `Diagnostics.Process` appears in `supervise.ps1` to start the rig and
  `Get-Process -Id` in `waitrun.ps1` to wait on a recorded pid.

## Running it

Nobody ran it when this record was written; what follows is read from the
files. `batch.ps1` with a scenario list and a family, then `summarize.cjs`. The
scratch root is compiled in as `C:\Source\SixFive7\BrowserAI\.work\debugger-tools`
and the child is the main checkout's payload; a run from elsewhere needs both
changed and is a new measurement with a date of its own.

## How the stored copies differ from the ones that ran

The two SPDX lines were added to every file. Nothing else moved.
