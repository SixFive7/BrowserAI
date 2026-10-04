<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-04 -- row 152's headed half on a desktop nobody is looking at, and Q380's 50,000 px page

**What this is.** Two measurements the lifetime review lane took in one hold of
the suite lock, 02:22:24Z to 02:32:35Z on 2026-10-04, each run on a desktop of
its own that was never put on the screen: the headed arms of re-verification row
152 (Q382 b), and a full-page screenshot of a page 50,000 px tall through the
published `BrowserAI.Server.exe` (Q380 a). Taken at `@playwright/mcp` **0.0.83**,
`playwright-core` **1.64.0-alpha-1790635538000**, node **v24.21.0**, Chrome for
Testing **155.0.8059.12** (`chromium-1247`) and Firefox **156.0**
(`firefox-1553`), on Windows 11 Pro 10.0.26300, with `BrowserAI.Server.exe`
published from `e30380a`. **46 files, 143,891 bytes.** The rig is
[`docs/probes/2026-10-04-lifetime/`](../../probes/2026-10-04-lifetime/README.md).

## Cited by

| Record | What it takes from here |
|---|---|
| [re-verification row 152](../../../kb/re-verification.md) and [kb: configuration](../../../kb/playwright/configuration.md#what---enable-automation-changes-that-a-page-or-a-server-can-see----measured-2026-09-24) | `row152/`: the headed arms |
| [kb: tools and artifacts](../../../kb/playwright/tools-and-artifacts.md#a-full-page-screenshot-past-16384-px-repeats-its-first-16384-rows-in-chromium-and-firefox-refuses-one-past-32767-px----measured-2026-10-04), re-verification row 190 and [the hazard index](../../../HAZARDS.md#hazard-index) | `tall/`: the four runs and what was read out of the two images |

## What is here

| Path | What it holds |
|---|---|
| `desk-probe/` | The private desktop's positive control, 00:43Z: the probe's own desktop name, and the census that found its window on the private desktop and none on the other |
| `row152/<arm>/` | Per arm: `fingerprint.json` (the 43 page-visible properties and every request's headers), `fp.log`, `config.json` for the two funnel arms, and `windows.log`, the census of every visible top-level window the arm's processes owned, once a second, on both desktops |
| `tall/<family>-<run>/` | Per run: `calls.log` (every call, its time and the start of its answer), `result.trimmed.json`, and `windows.log` (`calls.trimmed.log` for the two Firefox runs); for the two Chromium runs `bands.json` (which band each 100 px row of the image shows) and `rows.json` (whether each row at or below 16,384 px is the row 16,384 px above it) |
| `tall/chromium-r1/crop-*.png` | 640x400 crops across the seams at 16,384 and 32,768 px. The two files are byte-identical, because the image repeats |
| `originals.sha256`, `left-out.sha256` | What was trimmed and what was left out, below |

**No census recorded a `LEAK`**: in all 29 runs of the hold, no visible window
any of their processes owned appeared on the desktop the maintainer uses. The two 50x50 windows of class `UAC Input Indicator` and
`UAC_InputIndicatorOverlayWnd` that the census lists beside every browser are
owned by the browser process and sat on the private desktop.

## What was cut

- **The user profile path**, `C:\Users\<name>`, replaced by `%USERPROFILE%` in
  five files, stored under `.trimmed.` names with the originals' digests in
  `originals.sha256`.
- **Terminal colour escapes**, four of them, removed from the two Firefox runs'
  `calls.log`, which quote Playwright's error with its call log; stored as
  `calls.trimmed.log`, with the originals' digests in `originals.sha256`.
- **Left out whole, with digests in `left-out.sha256`**: the two full-page PNGs,
  2,274,703 bytes each and byte-identical to each other, and the four runs'
  `BrowserAI.Server.exe` stderr.
- **The other runs of the same hold** -- the headed and headless switch, closing
  every tab, closing a headed window, and resume conflicts -- are not here.
  Nothing in the tree cites them yet.

## Privacy

Every page was served from `127.0.0.1` by the rig, the only account was the rig's
own `tester`, and no file here holds a cookie value from anywhere else.
