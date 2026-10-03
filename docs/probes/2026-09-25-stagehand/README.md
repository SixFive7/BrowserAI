<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-25 -- one Chrome for Testing, driven by Playwright in BrowserAI's configuration and by Stagehand

Establishes
[What a snapshot and a click cost through BrowserAI's configuration](../../../kb/playwright/tools-and-artifacts.md#what-a-snapshot-and-a-click-cost-through-browserais-configuration----measured-2026-09-25)
and re-verification row 175. Evidence:
[`docs/evidence/2026-09-25-stagehand/`](../../evidence/2026-09-25-stagehand/README.md).

**Why it exists.** The zoom-out's track D compared Stagehand 4 with Playwright.
Stagehand was dropped; the Playwright half of each bench measured BrowserAI's own
configuration, and that half is why the bench is kept.

## What is here

| File | What it does |
|---|---|
| `bench-snap-pw.mjs`, `bench-snap-sh.mjs`, `summarize-snap.mjs`, `verify-noboxes.mjs` | Snapshot every page with each tool, boxes on and off, count the tokens and the time; and check that a per-call `boxes: false` leaves no box |
| `knobs.mjs` | The per-call boxes override and the settle wait at 500, 100 and 0 ms, with the clicks counted by the page |
| `bench-launch.mjs`, `bench-profile.mjs`, `bench-profile-short.mjs`, `bench-mem.mjs`, `bench-action.mjs`, `bench-dialog.mjs`, `bench-fp.mjs` | Launch, close and the profile's exit type, memory, actions, a page dialog, and what a page can see |
| `port-probe.mjs`, `port-probe-pw.mjs`, `port-probe.ps1` | Whether each tool's browser listens on a debugging port another process can read |
| `lib.mjs`, `pages.mjs`, `mcpclient.mjs`, `pwmcp.mjs`, `launch-sh-child.mjs`, `netlog-pw.mjs`, `pw-smoke.mjs`, `sh-smoke.mjs` | The shared pieces: the page server, a stdio MCP client, the Playwright child as BrowserAI starts it, Stagehand in a child, and the network log |
| `fixtures/` | The local pages: a form, a 500-row table, iframes, shadow roots, a dialog, a blank page and a fingerprint page |

## What keeps it off the rest of the machine

- Every browser is headless, from a scratch browsers folder, with scratch
  profiles, a scratch registry and a scratch `LOCALAPPDATA`.
- **It selects no process by image name.** Of the eight spellings
  [`ProcessSelection`](../../../tests/BrowserAI.Tests/Harness/ProcessSelection.cs)
  keys on, `Win32_Process` appears in `bench-mem.mjs`, `lib.mjs` and
  `port-probe.ps1`, each listing the machine's processes once and keeping the
  tree under a pid the bench launched. ⚠️ **The real scan flags `bench-mem.mjs`
  anyway**, measured by copying the rig under `build/`: it labels each process
  of that tree by its image name for the memory breakdown. A false positive;
  [the directory's README](../README.md) records it.
- Three benches load public pages: Wikipedia, Hacker News and GitHub.

## Running it

Nobody ran it when this record was written; what follows is read from the
files. Each bench is `node <bench>.mjs`, with the scratch root compiled in as
`C:\Source\SixFive7\BrowserAI\.work\zoomout\d\bench`; Stagehand 4.1.0 and the
browser have to be installed there first. A run is a new measurement with a date
of its own, and the public pages will have changed.

## How the stored copies differ from the ones that ran

The two SPDX lines were added to every script. The fixtures are as they were.
