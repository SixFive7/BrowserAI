<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-10-03 - first-run provisioning at chromium 1247 and firefox 1553

**The re-establishment of [re-verification row 21](../../../kb/re-verification.md)
on the `@playwright/mcp` 0.0.82 -> 0.0.83 roll**, taken in the batch that reviewed
the roll and not left owed, because the product quotes these figures:
`BrowserProvisioner.FirstRunDownloadBytes` is what every caller refused during
provisioning reads. The rig is
[`docs/probes/2026-09-16-provisioning`](../../probes/2026-09-16-provisioning/README.md);
the figures it produced are in
[kb: first-run provisioning](../../../kb/playwright/provisioning-and-timings.md#first-run-provisioning)
and
[Firefox, measured the same way](../../../kb/playwright/provisioning-and-timings.md#firefox-measured-the-same-way----2026-08-19).

## What is here

| File | What it is |
|---|---|
| `chromium-1.json`, `chromium-2.json` | Two clean Chromium provisions into an empty root, each the probe's own output: exit code, stopwatch seconds, total bytes and files, and the per-component breakdown |
| `firefox-1.json`, `firefox-2.json` | The same two runs for Firefox |
| `download-urls.json` | What `Get-DownloadUrls.ps1` derived from the rebuilt payload's **own** `browsers.json`, so that no revision in the wire figures was typed |
| `wire-content-length.txt` | The `HEAD` on each of those four URLs as the driver logged it, and the two family totals summed from them. This is where `208,824,056` and `130,935,881` come from |

**Nothing is cut and nothing is trimmed.** The six data files are whole and are
exactly what the rig and its driver wrote, with this repository's LF
normalisation and nothing else, so there is no digest of an original to record.

⚠️ **What is deliberately NOT here: the installer's own output**, for the reason
the 2026-09-21 batch gives: upstream colours its download line with ANSI escape
bytes, which this repository refuses in any text file. What it said, with the
escapes removed by hand for reading only, is *"Downloading Chrome for Testing
155.0.8059.12 (playwright chromium v1247) from
https://cdn.playwright.dev/builds/cft/155.0.8059.12/win64/chrome-win64.zip"*,
which is the URL `download-urls.json` derived, to the character. That line was read
from the payload build's own log, which installed the same revision into the
shared browsers root the same night.

## What the run found

**Chromium moved, for the first roll since 1244.** 1247 carries
`browserVersion` **155.0.8059.12** where 1244, 1245 and 1246 all carried
154.0.8037.0, and `cftUrl()` keys the archive on the version: 207,283,631 B on the
wire against 205,733,764 (`+1,549,867`), and `chromium-1247` holds
**458,240,242 B across 307 files** against 454,699,952 across 308.

**Firefox 1549 to 1553 is a rebuild of the same 156.0**: `+1,682` B on the
wire and `+1,512` B on disk, the file count unchanged at 63. `ffmpeg` 1011 and
`winldd` 1007 are byte-identical, which is the control for the two that moved.

**Both pairs came back identical to each other** in every byte and file count,
which is the property the procedure asks for before a figure is recorded.

`.links` is 81 B in all four runs, against 69 B before. It holds the absolute
path of the `playwright-core` that asked for the install, and this payload sat in
a git worktree whose path is 12 characters longer.

## What it touched

**The scratch roots it was given and nothing else.** Each run emptied
`.work\rv-scratch\provisioning\root-<family>-<n>` and provisioned into it. Nothing
read or wrote `%LocalAppData%\BrowserAI` or `%LocalAppData%\BrowserAI.app`, and
`TEMP` was left alone. The trees of `root-chromium-1` and `root-firefox-1` were
moved afterwards into a private browsers root for the debugger measurements of
the same review, so that a Firefox
those measurements ran left registry values under a path nothing else uses.

## What is not here

**No per-phase breakdown**, for the instrument reason the rig's README records.

**No third or fourth run.** The stopwatch seconds -- Chromium 13.25 and 17.05,
Firefox 7.94 and 7.46 -- are `[MACHINE]` figures, and the spread inside the
Chromium pair is larger than anything a 1.5 MB difference in the archive explains.
