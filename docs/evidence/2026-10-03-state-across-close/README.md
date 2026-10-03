<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- what a session keeps across a browser close, and what brings it back (Q325, Q328)

**What this is.** The tables the state-across-close rig wrote on 2026-10-03:
what survives `browser_close` and a whole-child teardown, what the browsers'
own session-restore options bring back, what a saved storage state carries,
what a `browser_close` with no browser open does, what happens to the network
capture across a relaunch, and what each design costs. Taken at
`@playwright/mcp` **0.0.82** (`playwright-core` 1.64.0-alpha-1789764292000,
`chromium-1246`, `firefox-1549`) and **0.0.83** (1.64.0-alpha-1790635538000,
`chromium-1247`, `firefox-1553`), each child driven over stdio the way BrowserAI
drives it, headless, on Windows 11. **25 files beside this README, 239,023
bytes as cut.** The rig is a probe record at
[`docs/probes/2026-10-03-state-across-close`](../../probes/2026-10-03-state-across-close/README.md).

[`results/summary.txt`](results/summary.txt) is the file to read first: one
section per table, with medians and ranges.

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: provisioning and timings](../../../kb/playwright/provisioning-and-timings.md#what-a-session-keeps-across-a-browser-close-and-what-brings-the-rest-back----measured-2026-10-03) | Every number in the entry, and the correction of the earlier sentence about a `browser_close` with no browser open |
| [kb: re-verification](../../../kb/re-verification.md) | Row 171 |
| [kb: what is not established](../../../kb/not-established.md) | The rows on a killed browser's restore and on what a restored page re-runs |

## What is here

| Path | What it is |
|---|---|
| `results/summary.txt` | Every table below, summarised per version and family |
| `results/S-survival.tsv`, `S-timings-memory.tsv` | Store by store, before and after a `browser_close` and after a whole-child teardown (design B); and the timings and memory of each step |
| `results/S-q328b-close-without-browser.tsv` | A `browser_close` with no browser open: the processes it created, its time and its answer |
| `results/H-har.tsv` | The network capture's entries and digest after each close and relaunch |
| `results/R-restore.tsv`, `T-reopen.tsv` | Each restore option, what came back and what the first call cost; and the URLs a profile recorded |
| `results/P-caller-close-teardown-resume.txt` | The caller's own close, a teardown and a resume in a new child with the restore options |
| `results/D-storage-state.tsv`, `M-memory.tsv` | What a saved storage state carries and restores; and memory per state |
| `results/errors.tsv` | Every run that failed, with why |
| `results/after-check-*.trimmed.txt`, `registry-cleanup.txt` | The isolation checks after the runs, and the Firefox launcher values the cleanup removed |
| `plan-*.txt`, `batch-progress.txt`, `launched-pids.tsv` | The plans the batches ran, their progress, and every pid the rig launched with its creation time |
| `originals.sha256`, `left-out.sha256` | What was changed and what was left out whole, with digests |

## What was cut, and what was left out

- **The run directories under `runs\`**: 31,724 files and 4,806,003,545 bytes,
  almost all of it the browser profiles each run wrote and reopened. What was
  read from them is in the tables.
- **The `@playwright/mcp` 0.0.83 install, the npm cache and the temp folders**:
  196, 14 and 546 files, 18,733,936, 40,736,861 and 1,411,800 bytes.
- **Third-party source**: Chromium's session-restore files and the juggler
  files extracted from `firefox-1549` and `firefox-1553`.
- **An export of the real `HKCU` Mozilla key** taken after the work, and the
  baseline snapshots of the real Mozilla folders: they name the user's own
  Firefox installs and profile. Their digests are in `left-out.sha256`.
- **The profile path** in the two after-checks, replaced by `%USERPROFILE%`.

## Privacy

No file here names the user, the user profile or the machine. The privacy scan
before the commit found nothing, and its positive control found all nineteen
planted needles.
