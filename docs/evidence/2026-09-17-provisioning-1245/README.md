<!-- SPDX-FileCopyrightText: 2026 Jori Huisman -->
<!-- SPDX-License-Identifier: LicenseRef-BrowserAI-FSL-1.1-MIT-5yr -->

# 2026-09-17 - first-run provisioning at chromium 1245 and firefox 1548

**The re-establishment of [re-verification row 21](../../../kb/re-verification.md),
one day after the `playwright-core` pull-forward made it owed.** The rig is
[`docs/probes/2026-09-16-provisioning`](../../probes/2026-09-16-provisioning/README.md);
the figures it produced are in
[kb: first-run provisioning](../../../kb/playwright/provisioning-and-timings.md#first-run-provisioning)
and
[Firefox, measured the same way](../../../kb/playwright/provisioning-and-timings.md#firefox-measured-the-same-way--2026-08-19).

**Why it is kept when the 2026-09-16 pair was not.** That run's JSON was scratch
and went with `.work/`, which left the row standing on a rig and a set of numbers
in prose. These are the four files the numbers were read out of, unmodified.

## What is here

| File | What it is |
|---|---|
| `chromium-1.json`, `chromium-2.json` | Two clean Chromium provisions into an empty root, each the probe's own output: exit code, stopwatch seconds, total bytes and files, and the per-component breakdown |
| `firefox-1.json`, `firefox-2.json` | The same two runs for Firefox |
| `<family>-<n>.installer.log` | What upstream's installer wrote, UTC-stamped per line by the rig. It is four lines total because Node buffers a piped stdout, which is why the per-phase boundaries are not in the kb entry |

**Nothing is cut.** All eight files are whole, so there is no digest of an
original to record. The `.json` files are exactly what
`Measure-Provisioning.ps1` wrote; the `.log` files are exactly what it captured,
with this repository's LF normalisation and nothing else.

## What the run found

**Chromium 1244 to 1245 produced no difference at all.** `playwright-core`
builds Chromium's URL with `cftUrl()`, keyed on `browserVersion` rather than on
the revision, and 1245 carries the same `154.0.8037.0` as 1244 - so the archive
fetched is the same archive and `chromium-1245` holds **454,699,952 B across 308
files**, which is what `chromium-1244` held. The installer log confirms the URL
as a string rather than as something derived: *"Downloading Chrome for Testing
154.0.8037.0 (playwright chromium v1245) from
https://cdn.playwright.dev/builds/cft/154.0.8037.0/win64/chrome-win64.zip"*.

**Firefox 1544 to 1548 moved by 327 bytes on the wire and 902 on disk**, across
the same 61 files. `ffmpeg` 1011 and `winldd` 1007 are byte-identical, which is
the control for the two revisions that did move.

**Both pairs came back identical to each other**, which is the property the
procedure asks for before a figure is recorded.

## What it touched

**The scratch root it was given and nothing else.** Each run emptied
`.work\2026-09-17-o4\<family>-root` and pointed the child's
`PLAYWRIGHT_BROWSERS_PATH` at it. It never read or wrote
`%LocalAppData%\BrowserAI` or `%LocalAppData%\BrowserAI.app`, and it left `TEMP`
alone, which is the predicate every earlier measurement in this series was taken
under. The scratch root was removed afterwards; these files are the only thing
that survived it.
