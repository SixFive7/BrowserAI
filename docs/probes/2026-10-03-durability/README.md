<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- commit sooner, kill, launch again, read back

Establishes
[Committing to disk sooner, and session restore after a hard kill](../../../kb/playwright/provisioning-and-timings.md#committing-to-disk-sooner-and-session-restore-after-a-hard-kill----measured-2026-10-03),
the [entry on a job's I/O counters](../../../kb/windows/job-objects.md), and
re-verification rows 182 to 185. Evidence:
[`docs/evidence/2026-10-03-durability/`](../../evidence/2026-10-03-durability/README.md).

**Why it exists.** The maintainer asked whether both browsers can be told to
commit everything to disk at once, and a client ends a session by killing the
tree, so this rig measures each store's window with and without every lever
found, and what each lever costs. It grew from
[the hard-kill rig](../2026-10-03-hard-kill/README.md) and reuses its job
helper.

## What is here

| File | What it does |
|---|---|
| `orchestrate.ps1` | Runs a plan, one browser alive at a time: a launch and clean close to warm the profile, the writer in a kill-on-close job it creates, a watcher over each store's files, the kill by closing that job `D` seconds after the page reports its writes or after a lever's call answers, an inspection of the killed profile, an optional second session and second kill, and a reader on the same profile; one JSON line per run. A workload run reads the job's I/O counters and the page's timer lag over 60 s instead |
| `hk.cs` | The job object, its I/O counters, the machine's idle time, the launch and the process-tree reading the orchestrator compiles with `Add-Type` |
| `childenv.ps1` | The child environment BrowserAI builds, with the browsers path, `TEMP`, `TMP`, `LOCALAPPDATA` and Playwright's registry moved into the scratch |
| `driver.js` | The writer, the reader and the workload driver through `playwright-core` with BrowserAI's launch options, and the levers: the DevTools calls, the 512 cookies, the page close and the form fills |
| `server.js` | The page server on `127.0.0.1`: the write, tab, read, storage-workload and form pages, and the log of what each run's pages reported |
| `inspect.js` | Reads a killed profile's session files and Chromium's `Preferences` before any relaunch, and rewrites `profile.exit_type` for the `patch` lever |
| `integrity.js` | Copies each SQLite store of the reopened profile with its siblings and runs `PRAGMA integrity_check` on the copy |
| `chain.sh` | Runs plans one after another |
| `make-plans.py`, `add-extra.py`, `add-extra2.py`, `add-extra3.py`, `add-extra4.py`, `make-supp.py` | Write the plans; the `add-extra` scripts appended cells to plans that had not started yet |
| `summarize.py`, `ages.py`, `table.py`, `cells.py`, `firstsave.py`, `cyclecheck.py`, `fxsess.py`, `formcost.py`, `lsevents.py` | Read the results into the summaries the evidence holds |
| `cite.py` | Prints every source range the kb entry cites, from the files the research fetched |
| `iocount.ps1`, `iocount.js` | The job-counter check: a node process in a kill-on-close job writes 32 MiB to a pipe, 32 MiB to a file, or nothing |
| `run-in-job.ps1`, `smoke-cr.js`, `smoke-cr2.js` | The first smoke tests of the launch, kept because they ran |
| `mozlz4-ref.js` | A `mozLz4` reader copied from the state-across-close rig as a reference for `inspect.js` |
| `snap-registry.ps1`, `registry-cleanup.ps1` | A read-only snapshot of the three Firefox keys, the Chrome for Testing keys and the Mozilla folders; and the removal of only the values absent from that snapshot that name the rig's own Firefox |
| `pre-supp/` | `orchestrate.ps1`, `driver.js` and `server.js` as they ran the first agent's plans, before the second agent's additions |

## What keeps it off the rest of the machine

- Every browser is headless and runs from the research's own copy of
  `chromium-1247` and `firefox-1553` in the scratch, with `TEMP`, `TMP`,
  `LOCALAPPDATA` and Playwright's registry in the scratch as well.
- It ends only what it launched, by closing a job object it created. **It
  selects no process by image name.** Of the eight spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on, it carries the `szExeFile` field of the toolhelp struct `hk.cs`
  declares for its parent-pid walk, and `taskkill` in a comment. ⚠️ **The real
  scan flags `hk.cs` anyway**, measured by copying the rig under `build/` with
  `observe.ps1` beside it as the positive control, which the scan named too, and
  removing both after: the field declaration is a false positive, as it is in the
  hard-kill rig. [The directory's README](../README.md) records it.
- A Firefox run leaves seven values under the three Firefox keys that name the
  executable it ran from; `registry-cleanup.ps1` removes those and nothing else,
  and its record is in the evidence.
- `--hide-crash-restore-bubble` and the `exit_type` rewrite act on the rig's own
  profiles only.

## Running it

What follows is read from the files; the second agent ran `chain.sh supp` this
way on 2026-10-03. The scratch root is compiled in as
`C:\Source\SixFive7\BrowserAI\.work\durability`, with the rig under `rig\`, a
copy of the payload under `payload\` and the browsers under `browsers\`. Write
the plans with `make-plans.py` and `make-supp.py`, then run
`bash rig/chain.sh <plan> ...` from that root; `chain.sh` starts
`orchestrate.ps1`, which starts `server.js` on port 47911. The summary scripts
then read `results\`. `iocount.ps1` expects itself and `iocount.js` under
`iocount\`. A run from anywhere else needs the root changed and is a new
measurement with a date of its own.

## How the stored copies differ from the ones that ran

- The two SPDX lines were added to every file but `iocount.ps1` and
  `iocount.js`, which had them, after the shebang in `chain.sh`. Line numbers
  here are the stored files'; in the files that ran, each was two lines earlier.
- Three PowerShell casts gained a space before their parenthesis, because the
  link check reads a bracket followed by a parenthesis as a Markdown link: the
  `[int]` cast on `$status.writeHits` in `orchestrate.ps1` and in
  `pre-supp/orchestrate.ps1`, and the `[string[]]` cast in `run-in-job.ps1`.
  PowerShell casts the same way with the space.
- `orchestrate.ps1`, `driver.js` and `server.js` here are the versions that ran
  `supp` and `smoke-form`, and they differ from `pre-supp/` by additions only:
  the form page and its route, a fourth argument to `WORK` that defaults to the
  old page, and two fields in each survival record, `tWriterLaunched` and
  `idleMsAtLaunch`.
