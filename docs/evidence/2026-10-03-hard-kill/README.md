<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 -- how old a browser's writes must be before a hard kill keeps them (Q356 a)

**What this is.** What the hard-kill rig recorded on 2026-10-03 between about
01:29Z and 05:55Z: a page in a persistent profile writes cookies,
`localStorage`, IndexedDB and `sessionStorage`; the browser tree is killed a
chosen number of seconds later, by its job or by `taskkill /T /F`, or the
`@playwright/mcp` child's stdin is closed; the profile is relaunched and read
back. Plus the leftovers of a hard-killed session holder and the registry reap.
Taken at Chrome for Testing **154.0.8037.0** (`chromium-1246`), Firefox
**156.0** (`firefox-1549`), `playwright-core` **1.64.0-alpha-1789764292000**
and `@playwright/mcp` **0.0.82**, from the payload and on its node, on
Windows 11. **82 files beside this README, 1,720,979 bytes as cut.** The rig is
a probe record at
[`docs/probes/2026-10-03-hard-kill`](../../probes/2026-10-03-hard-kill/README.md).

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: provisioning and timings](../../../kb/playwright/provisioning-and-timings.md#how-old-a-write-must-be-before-a-hard-kill-keeps-it----measured-2026-10-03) | Every number in the entry |
| [kb: re-verification](../../../kb/re-verification.md) | Row 170 |
| [kb: what is not established](../../../kb/not-established.md) | The rows on the second write and the flushing switch |

## What is here

| Path | What it is |
|---|---|
| `results/survival-summary.txt` | The result: one line per browser, path, kill, delay and variant, with how many runs kept each store and whether the profile reopened intact. The failed runs are listed at its end |
| `results/survival.tsv` | The same, one line per run |
| `results/<plan>.jsonl`, `.log` | One JSON line per run of each plan, with every timing, the process tree before the kill, what joined it during a grace, and what was read back; and the orchestrator's log |
| `results/chain-*.txt` | The order the plans ran in, with start and end times |
| `results/reap-test.tsv`, `.log` | Playwright's registry descriptors before and after the reap, per run |
| `results/sim-session.json`, `.log` | Three stand-ins for a BrowserAI session's holders, killed hard: when the lock could be opened again, what files were left, and what the next reader saw |
| `results/launcher-removed.trimmed.tsv` | The five values the scratch Firefox left under `HKCU\Software\Mozilla\Firefox\Launcher`, which the cleanup removed, with the profile path cut |
| `server-logs/` | The rig's own page server, one log per sitting |
| `plans/` | Every plan the orchestrator ran: browser, path, delay, kill, repetition and variant per run |
| `originals.sha256`, `left-out.sha256` | What was changed and what was left out whole, with digests |

**Variants.** `core` drives `playwright-core` directly with BrowserAI's launch
options; `mcp` goes through a real `@playwright/mcp` child. `aggr` adds
`--enable-aggressive-domstorage-flushing`. `warm` launches, writes and closes
the profile once before the measured run. `second` makes a second write that
many seconds after the first, and `D` is then counted from the second.
`toolclose` calls `browser_close` before the kill, and `eof` closes the child's
stdin and kills the tree after a grace. `fast` skips the integrity check.

## What was cut, and what was left out

- **The per-run directories under `runs\`**: 597 files, 92,850,743 bytes, the
  browser profiles and temp folders each run wrote and read back. What was read
  is in the results.
- **`results/external-snapshots.txt`**, and the whole of `baseline\`: listings
  of the real Mozilla folders and of the real Firefox launcher key, taken to
  show the work did not touch them. They name the user's own Firefox profiles.
  Their digests are in `left-out.sha256`.
- **The source files read**: Chromium's and Firefox's under `src-read\` and
  Playwright's under `upstream-check\`, with the `playwright-core` package
  extracted to compare two builds. The kb entry names file and line.
- **A draft upstream report and its duplicate search**, which are for the
  maintainer.
- **The three simulated session directories**, whose content
  `sim-session.json` lists.
- **The profile path** in `launcher-removed.tsv`, replaced by `%USERPROFILE%`,
  stored under a `.trimmed.` name.

## Privacy

No file here names the user, the user profile or the machine. The privacy scan
before the commit found nothing, and its positive control found all nineteen
planted needles.
