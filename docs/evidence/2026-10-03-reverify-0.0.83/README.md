<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 - the re-verification rows the `@playwright/mcp` 0.0.83 roll left stale

**What this is.** The records behind re-taking the rows of
[the re-verification index](../../../kb/re-verification.md) that the 0.0.83 review
of that morning stamped `[STALE]`: 6, 22, 32, 34, 38, 95, 103, 109, 115, 121's
Chromium half, 122, 127's window time, 140, 141, 152's headless half and 163 to
166, and the records rows 5 and 66 are stamped from. Taken 2026-10-03 between
13:17Z and 13:46Z, plus the raw half of row 152 later that evening, at
`@playwright/mcp` **0.0.83**, `playwright-core` **1.64.0-alpha-1790635538000**,
node **v24.21.0**, Chrome for Testing **155.0.8059.12** (`chromium-1247`) and
Firefox **156.0** (`firefox-1553`), on Windows 11. The payload was the review's
own, and every BrowserAI process was published from `5f1166c`.

Every arm that started a browser, a server or a rig ran under the suite lock.
Every arm was headless except the three rows that exist to measure a window,
127, 32 and 115, which the maintainer approved that afternoon in his words
*"Q374 a I am back now. Proceed"* and which were run one at a time with him at
the desk. **What they put on the screen is the section below.**

## Cited by

| Record | What it takes from here |
|---|---|
| [kb: re-verification](../../../kb/re-verification.md) | Rows 5, 6, 22, 32, 34, 38, 66, 95, 103, 109, 115, 121, 122, 127, 140, 141, 152 and 163 to 166 |
| [kb: job objects](../../../kb/windows/job-objects.md) | The breakaway callers at Chromium 155 and Firefox 156 |
| [kb: fingerprinting](../../../kb/chromium/fingerprinting.md) | The `--browser-test` call sites, and the user agent and `navigator.webdriver` through the config |
| [kb: detection](../../../kb/windows/detection.md) | Two browsers on one profile directory |
| [kb: profiles](../../../kb/chromium/profiles.md) | The unusable `--user-data-dir`, the data-directory dialog, and the cookie store read with DPAPI |
| [kb: provisioning and timings](../../../kb/playwright/provisioning-and-timings.md) | The cost ratios and both resume paths |
| [kb: processes](../../../kb/windows/processes.md) | Renames under live browsers, and the show-window flag against the foreground |
| [kb: configuration](../../../kb/playwright/configuration.md) | The network-service sandbox arms, the fallback profile directory, the corrupted executable and `--enable-automation` |
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md) | The WebP bracket, screenshot bytes across the roll, and the four dashboard rigs |
| [kb: Velopack](../../../kb/packaging/velopack.md) | The configuration app's time to its window |

## What is here

| Path | What it holds |
|---|---|
| `runs/batch1/` | Rows 140, 121, 22, 141, 122, 109, 152 (the product funnel) and 95, as the batch driver ran them, one log per row and the batch's own timeline |
| `runs/batch2/` | Row 103: the three rename scripts' logs and the browsers root listed before and after |
| `runs/batch3/` | Rows 34 and 38: one log per round or run and the timeline, the six extra Path A runs included |
| `runs/batch4/` | Rows 163 to 166: the four dashboard rigs' logs |
| `row34/`, `row38/` | What `ratios-probe.js`, `resume-probe2.js` and `selfdeath-probe.js` wrote per round, with the servers' stderr tails |
| `row109/`, `row121/`, `row122/`, `row32/`, `row95/`, `row115/`, `row127/`, `row152/` | The per-row records the rigs wrote beside their logs: configs, results, stderr captures, the foreground logs and the window times |
| `rows5-6/` | The Chromium and Firefox source reads: the two tags and their commits, every `CREATE_BREAKAWAY_FROM_JOB` and `force_breakaway_from_job` line at both Chromium tags, the isolated-browser gate, the Firefox callers at `mozilla-firefox` `3bf8f468`, Playwright's r1553 patches, and the `switches::kBrowserTest` call sites at both tags with their context normalised |
| `rows163-166/` | What the dashboard rigs wrote: results, the owner's trace, the screenshots and the registry listings |
| `rv-gate/` | The 0.0.83 review's own gate records rows 5 and 66 rest on: both rounds' timelines with the `ms-playwright-mcp` test at the end of each, the second round's driver output, the browsers root before and after each round, the two containment records, and the driver that wrote them |
| `registry/` | `HKCU\Software\Mozilla\Firefox\Launcher` before and after the Firefox arms, and `HKCU\Software\Google` around row 32 |
| `rig/` | Every rig as it ran, the batch drivers, the lock helper, and the scripts that inventoried and cut this batch |
| `originals.sha256` | Every file stored changed, with the digest of the original and what changed |

**The rigs are the recorded ones, pointed at the new revisions.** Rows 22, 34,
38, 103, 122, 140, 141, 152 and 163 to 166 ran the scripts in `docs/probes/` or
in the earlier evidence batches with the revision constants, the repository path
and the scratch locations changed, which is what `rig/patch_rigs.py` did;
rows 32, 95, 109, 115, 121 and 127 were written that day for the procedure the
row states.

## What appeared on the screen, and for how long

All times are UTC on 2026-10-03, read off the rigs' own logs. Nothing else in this
batch opened a window.

| Row | When | What was on the screen |
|---|---|---|
| 127 | 13:39:32, 13:39:34 and 13:39:36 | The configuration app's task dialog, titled *BrowserAI*, 556 by 444, three times about two seconds apart. Each was found 843, 534.7 and 460.6 ms after the app started, `WM_CLOSE` was posted to it within 4 ms, and the app had exited 0 within about 0.15 s of the sighting, so each dialog was up for well under a second |
| 32 | 13:41:29 to 13:41:41 | The direct launch against a path occupied by a file: Chrome's *Failed to create data directory* dialog, one OK button, from 0.2 s after the launch until the rig posted `WM_CLOSE` at 6.1 s, then whatever the browser did until the rig ended its tree at 11.8 s. This run's reader of the window state failed, so what showed after the close is not recorded, and the arm was run again |
| 32 | 13:42:20 to 13:42:33 | The same arm again: the dialog from 0.2 s to 6.6 s, then a headed *about:blank - Google Chrome for Testing* window on the fallback profile, seen 4 s after the close and up until the rig ended the tree at 12.8 s |
| 32 | 13:40:03 to 13:40:34, and 13:41:29 | The other two arms, the fallback through `cli.js` and the deny-all directory, were headless and showed nothing |
| 115 | 13:43:16 to 13:43:43 | Four headed browsers one after another, each up about 5 to 6 s and then ended by pid: Firefox with the show-window flag, Firefox without it, Chrome for Testing with it, and without it. Firefox showed an untitled dialog-class window and then a window titled *Nightly*; Chrome for Testing an *about:blank* window. Both Firefox windows and the flagless Chrome took the focus. In this run the window in front was not an ancestor of the launcher, so it is not the measurement |
| 115 | 13:45:08 to 13:45:35 | The same four again, about 6 s each, with VS Code in front, which is the measurement: the same two Firefox windows and the flagless Chrome took the focus, and the flagged Chrome showed its window without taking it |
| 115 | 13:45:57 to about 13:46:23 | One more Firefox with the flag, to repeat the result. Its window took the focus at 1.1 s; the run was cut off before the rig ended the browser, and the lane ended that browser's process tree by pid about 24 s after it opened |

The maintainer wrote at 13:43Z that browsers and dialogs kept showing up, which
is these rows. The lane released the suite lock at 13:46:41Z and was stopped at
13:49:55Z, with nothing it had started still running.

## What was cut, and what was left out

- **The user-profile path**, replaced by `%USERPROFILE%`, in 41 files, and the
  user name where `ls -l` printed it as the owner, in one. Those files are stored
  under a `.trimmed.` name and `originals.sha256` carries each original's digest.
- **Terminal escapes**, in `runs/batch1/row140.trimmed.log`, where Playwright
  colours its call log.
- **The titles of windows outside the launched tree**, in the three foreground
  logs of row 115: whenever the foreground belonged to another program on the
  desktop, the log keeps its handle, pid and class and the title is replaced by
  `<cut>`. The launched browsers' own titles are kept.
- **The two-line SPDX header**, added to every rig file of a kind the header
  rule covers, under the same name; the original's digest is in
  `originals.sha256`.
- **Left out by directory**: every browser profile and session directory the rigs
  created, the scratch browser trees of row 140, the scratch copy of
  `chromium-headless-shell-1247`, the MCP output directories, the scratch client
  configuration row 127 handed the app, and the two Chromium and one Firefox
  source checkouts the source reads ran against (fetched at depth 1:
  `chromium/src` tags `152.0.7977.8` at `690eeba1` and `155.0.8059.12` at
  `384c8207`, `mozilla-firefox` at `3bf8f468`).
- **Left out whole**: `batch1-driver.log` and `batch3-driver.log`, which repeat
  the batch timelines kept under `runs/`; and an earlier comparison file for row
  6 that compared the context with its line numbers only partly removed, replaced
  by the two `row6-context-normalised-*` files.
- ⚠️ **Five of the six extra Path A runs of row 38 have their logs and not their
  JSON.** The driver of those six handed every run one literal output path, so
  each run's JSON overwrote the last and only `A-chromium-4.json` survived. The
  readings, resume and navigate times and the stores before and after, are in
  each run's log, which is what the kb quotes; the servers' stderr tails, which
  only the JSON held, are lost for those five runs.

## What it touched

- **The shared browsers root**, which row 103 renames: `chromium-1247`,
  `firefox-1553`, `ffmpeg-1011`, `winldd-1007` and the root itself, each put back
  in a `finally`, with the root listed identically before and after
  (`runs/batch2/batch2.log`). That is why the row runs only under the suite lock.
- **`HKCU\Software\Mozilla\Firefox\Launcher`**: no value added and none
  removed. The two values Firefox keeps for `firefox-1553\firefox\firefox.exe`,
  `Launcher` and `Browser`, which the morning's review had created, were
  rewritten with new timestamps by the Firefox arms, as every launch does. They
  were left as they were.
- **`HKCU\Software\Google\Chrome for Testing`**: the keys existed before
  (`PreferenceMACs` since 2026-07-21, the key itself since 2026-08-29), and the
  two row 32 arms that fell back to the default profile rewrote values under
  `PreferenceMACs\Default` and `StabilityMetrics`. Nothing was added at the key
  level.
- **`%LOCALAPPDATA%\Google\Chrome for Testing\User Data`**, the fallback profile
  row 32 exists to show, was created by those two arms and removed by the lane
  afterwards; it was absent again that evening.
- **`%LOCALAPPDATA%\BrowserAI`**: the sessions rows 34, 38 and 95 created were
  destroyed through `browserai_destroy`, and their servers wrote to the shared
  process log.

## Privacy

Nothing here names the user, the user profile or the machine. The window titles
kept are the browsers' own and the configuration app's. Row 95's reader recovered
a cookie set by a loopback page server with a made-up value; no key material is
stored, only the key's length and prefix.
