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

**Nothing is cut and nothing is trimmed.** All four files are whole and are
exactly what `Measure-Provisioning.ps1` wrote, with this repository's LF
normalisation and nothing else, so there is no digest of an original to record.

⚠️ **What is deliberately NOT here: the installer's own output.** The rig
captures it and it is what confirms the derived URLs against the string upstream
actually prints, but upstream colours that line with ANSI SGR escapes - `0x1B`
bytes - and `HouseRuleTests.NoTextFileInTheTreeCarriesAControlByte` refuses a C0
control byte anywhere in this repository's text. **It is dropped rather than
stripped**, because a doctored capture is worth less than a quoted line: what it
said, with the escapes removed by hand for reading only, is *"Downloading Chrome
for Testing 154.0.8037.0 (playwright chromium v1245) from
https://cdn.playwright.dev/builds/cft/154.0.8037.0/win64/chrome-win64.zip"* and
*"Downloading Firefox 155.0 (playwright firefox v1548) from
https://cdn.playwright.dev/dbazure/download/playwright/builds/firefox/1548/firefox-win64.zip"*.
Re-running the rig produces it again; the file it writes is `<OutJson>.log`.

## What the run found

**Chromium 1244 to 1245 produced no difference at all.** `playwright-core`
builds Chromium's URL with `cftUrl()`, keyed on `browserVersion` rather than on
the revision, and 1245 carries the same `154.0.8037.0` as 1244 - so the archive
fetched is the same archive and `chromium-1245` holds **454,699,952 B across 308
files**, which is what `chromium-1244` held. The installer confirmed that URL as
a string rather than as something derived, in the line quoted above.

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
