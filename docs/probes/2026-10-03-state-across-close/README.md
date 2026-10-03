<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- set every kind of state, close the browser, read it back

Establishes
[What a session keeps across a browser close, and what brings the rest back](../../../kb/playwright/provisioning-and-timings.md#what-a-session-keeps-across-a-browser-close-and-what-brings-the-rest-back----measured-2026-10-03)
and re-verification row 171. Evidence:
[`docs/evidence/2026-10-03-state-across-close/`](../../evidence/2026-10-03-state-across-close/README.md).

**Why it exists.** Q325 and Q328: before deciding how an idle close and a
resume should behave, measure what a close loses, what the browsers can restore
by themselves, and what a close with no browser does.

## What is here

| File | What it does |
|---|---|
| `scenarios.js` | One run of one scenario at one version and family: a page server, a real `@playwright/mcp` child started the way BrowserAI starts one (`node cli.js --config <file> --sandbox`, the session directory as its working folder), the state set by the page, the close or teardown, and the read-back. The restore variants are the launch options it writes into the child's config |
| `lib.js` | The child, the page server and the stdio driver the scenarios share |
| `batch.js`, `launch.ps1` | Run a plan one run at a time, detached, with output to files |
| `jobctl.ps1` | Holds the job object every process of a run lives in, and answers one JSON command per line |
| `aggregate.js`, `summarise.js`, `show.js` | The TSV tables, the summary, and one run's steps |
| `mozlz4.js` | Decodes Firefox's `sessionstore.jsonlz4` to list the tabs it would restore |
| `install083.ps1` | Installs `@playwright/mcp` 0.0.83 into scratch beside the payload's 0.0.82 |
| `after-check.ps1` | The isolation checks after the runs, and the removal of the Firefox launcher values this rig added; it reports only unless `-Remove` is passed |

## What keeps it off the rest of the machine

- Every browser is headless, under a scratch registry, scratch temp folders and
  the shared scratch browser cache. The real `ms-playwright` registry was only
  read.
- Every process of a run lives in one job the rig holds, and is checked by its
  recorded pid and creation time. **It selects no process by image name**: the
  one spelling of the eight
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on is `Get-Process -Id` in `after-check.ps1`, a pid lookup.
- A Firefox run leaves five values under
  `HKCU\Software\Mozilla\Firefox\Launcher`; `after-check.ps1 -Remove` takes out
  the ones this rig added and nothing else.

## Running it

Nobody ran it when this record was written; what follows is read from the
files. `node batch.js <plan>` with a plan from the evidence's `plan-*.txt`, then
`node aggregate.js` and `node summarise.js`. The scratch root is compiled in as
`C:\Source\SixFive7\BrowserAI\.work\state-across-close`, and the 0.0.82 child is
the main checkout's payload; a run from elsewhere needs both changed and is a
new measurement with a date of its own.

## How the stored copies differ from the ones that ran

The two SPDX lines were added to every file. Nothing else moved.
