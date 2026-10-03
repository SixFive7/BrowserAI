<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- write, wait, kill, read back

Establishes
[How old a write must be before a hard kill keeps it](../../../kb/playwright/provisioning-and-timings.md#how-old-a-write-must-be-before-a-hard-kill-keeps-it----measured-2026-10-03)
and re-verification row 170. Evidence:
[`docs/evidence/2026-10-03-hard-kill/`](../../evidence/2026-10-03-hard-kill/README.md).

**Why it exists.** Q356 a: a client ends a session by killing the server's tree,
so what a browser keeps depends on what it had written when the kill landed.
This rig measures that window, store by store, for both families.

## What is here

| File | What it does |
|---|---|
| `orchestrate.ps1` | Runs a plan, one browser alive at a time: starts the writer in a `KILL_ON_JOB_CLOSE` job it creates, waits `D` seconds after the page reports its writes, kills the tree by terminating the job or with `taskkill /T /F /PID` on the root it launched, starts a reader on the same profile, and appends one JSON line per run. It refuses to kill a tree holding a process it cannot prove its own, and records the refusal |
| `hk.cs` | The job object, the launch and the process-tree reading the orchestrator compiles with `Add-Type` |
| `driver.js`, `driver2.js`, `driver3.js` | The writer and the reader through `playwright-core`, with BrowserAI's launch options |
| `server.js` | The page server every run writes to and reads from, on `127.0.0.1` |
| `integrity.js` | Copies each SQLite store of the reopened profile with its siblings and runs `PRAGMA integrity_check` on the copy, so the profile itself is never opened |
| `summarize.py`, `flushtimes.py`, `gracetimes.py` | The survival table, when each store file changed after a write, and the grace runs' timings |
| `reap-test.ps1` | Counts Playwright's registry descriptors before and after the reap |
| `sim-session.ps1`, `sim-session-store.js` | Without BrowserAI: one child holds `browserai.lock` the way `LockFile.Hold` does and another holds `browserai.data` the way `SessionStore` does, in WAL mode with one row in flight; both are in one kill-on-close job, which is closed, and the directory is read back |
| `chain.sh`, `locked-chain.sh` | Run plans one after another with a read-only snapshot of the outside state before and after each; the second takes the suite lock first and releases it on every way out |
| `check-leftover-procs.ps1`, `snap-external.ps1`, `launcher-cleanup.ps1` | Check by recorded pid and creation time that nothing of the rig is left running; snapshot the real Mozilla folders and the launcher key, read-only; and remove from that key only the values absent from the baseline that name the one Firefox executable the rig ran |

## What keeps it off the rest of the machine

- Every browser is headless and runs from the shared scratch browser cache,
  with the registry and the temp folders in scratch.
- It kills only trees it launched, and checks each member against its own job
  before a `taskkill`. **It selects no process by image name**; the eight
  spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on appear as `taskkill` with `/PID` and as the `szExeFile` field of the
  toolhelp struct `hk.cs` declares for its parent-pid walk. ⚠️ **The real scan
  flags `hk.cs`, `orchestrate.ps1` and `reap-test.ps1` anyway**, measured by
  copying the rig under `build/`: the field declaration, `taskkill` on lines
  without `/PID`, and a `Name -like` on a directory listing. All three are false
  positives; [the directory's README](../README.md) records them.
- A Firefox run leaves five values under
  `HKCU\Software\Mozilla\Firefox\Launcher`; `launcher-cleanup.ps1` removes the
  ones that name the scratch copy and nothing else.

## Running it

Nobody ran it when this record was written; what follows is read from the
files. Start `server.js`, then `orchestrate.ps1` with a plan from the evidence's
`plans\`, then `summarize.py` over the results. The scratch root is compiled in
as `C:\Source\SixFive7\BrowserAI\.work\hard-kill`; a run from elsewhere needs
it changed and is a new measurement with a date of its own.

## How the stored copies differ from the ones that ran

The two SPDX lines were added to every file, and `orchestrate.ps1` changed in
two places that do not change what it does. A comment at its line 428 read
*"refuse rather than risk it"* and now reads *"refuse, and do not risk it"*,
because this tree refuses that phrase in its own prose. And line 497's cast
`[long](($w...` gained a space after `[long]`, because the link check reads
that bracket-parenthesis pair as a Markdown link; PowerShell casts the same way
with the space. Line numbers here are the stored file's; in the file that ran,
each was three lines earlier.
